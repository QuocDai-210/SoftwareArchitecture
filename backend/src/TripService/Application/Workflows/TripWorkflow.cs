using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripWorkflow(
    ITripReader reader,
    ITripWriter writer,
    IMatchingClient matching,
    IEventPublisher bus,
    ITripMetrics metrics
)
{
    public async Task RecordFailure(Guid id, bool transient, CancellationToken ct)
    {
        var trip = await reader.Get(id, ct);
        if (
            trip.Status
            is not (
                TripStatus.Requested
                or TripStatus.ReservingDriver
                or TripStatus.AcceptingDriver
            )
        )
            return;
        if (transient && trip.WorkflowRetries < 3)
        {
            trip.ScheduleRetry(DateTimeOffset.UtcNow, Random.Shared.NextDouble());
        }
        else
        {
            trip.Move(TripStatus.Failed, "Không thể hoàn tất ghép xe. Hãy thử tạo chuyến mới.");
            await WorkflowEvents.Release(trip, bus, ct);
            metrics.Outcome("failed");
        }
        await writer.Save(ct);
    }

    public async Task Step(Guid id, CancellationToken ct)
    {
        var trip = await reader.Get(id, ct);
        using var activity = metrics.TraceWorkflow(id, trip.CorrelationId);
        switch (trip.Status)
        {
            case TripStatus.WalletHoldPending:
                await bus.Publish(WorkflowEvents.ReserveWallet(trip), trip.CorrelationId, ct);
                break;
            case TripStatus.Requested:
            {
                var match = (
                    await matching.Match(
                        new MatchRequest(trip.Pickup, trip.ExcludedDrivers.ToArray()),
                        trip.CorrelationId,
                        ct
                    )
                ).Driver;
                if (match is null)
                {
                    trip.Move(TripStatus.Failed, "Không có tài xế phù hợp trong 5 km.");
                    break;
                }
                var quote = await matching.Quote(
                    new QuoteRequest(trip.Pickup, trip.Destination),
                    trip.CorrelationId,
                    ct
                );
                trip.OfferQuote(match.DriverId, quote);
                break;
            }
            case TripStatus.ReservingDriver:
            {
                var reservation = await matching.Reserve(
                    new ReservationRequest(trip.Id, trip.ReservationId!.Value, trip.DriverId!),
                    trip.CorrelationId,
                    ct
                );
                if (reservation.Acquired)
                {
                    trip.OfferDriver(reservation.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(60));
                }
                else
                {
                    trip.ExcludeDriver();
                    trip.Move(TripStatus.Requested, "Tài xế vừa nhận chuyến khác. Đang tìm lại.");
                }
                break;
            }
            case TripStatus.AcceptingDriver:
            {
                var accepted = await matching.Confirm(
                    new ReservationRequest(trip.Id, trip.ReservationId!.Value, trip.DriverId!),
                    trip.CorrelationId,
                    ct
                );
                if (accepted.Acquired)
                {
                    trip.AcceptDriver();
                    await bus.Publish(
                        new TripAccepted(
                            Guid.NewGuid(),
                            trip.Id,
                            trip.DriverId!,
                            trip.ReservationId.Value,
                            trip.CorrelationId
                        ),
                        trip.CorrelationId,
                        ct
                    );
                }
                else
                {
                    trip.ExcludeDriver();
                    trip.Move(TripStatus.Requested, "Lời mời đã hết hiệu lực.");
                    await WorkflowEvents.Release(trip, bus, ct);
                }
                break;
            }
            case TripStatus.AwaitingDriverAcceptance when trip.Deadline <= DateTimeOffset.UtcNow:
                trip.RejectDriver();
                await WorkflowEvents.Release(trip, bus, ct);
                break;
            case TripStatus.AwaitingRiderConfirmation when trip.Deadline <= DateTimeOffset.UtcNow:
                trip.Move(TripStatus.Requested, "Báo giá hết hạn. Đang lấy giá mới.");
                break;
            default:
                return;
        }
        trip.ClearRetry();
        await writer.Save(ct);
        if (trip.Status == TripStatus.Failed)
            metrics.Outcome("failed");
    }
}
