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
    public async Task RecordFailure(Guid id, long version, bool transient, CancellationToken ct)
    {
        var trip = await reader.Get(id, ct);
        if (
            trip.Version != version
            || trip.Status
                is not (
                    TripStatus.Requested
                    or TripStatus.ReservingDriver
                    or TripStatus.AcceptingDriver
                    or TripStatus.AwaitingDriverAcceptance
                )
        )
            return;
        if (transient && trip.WorkflowRetries < 3)
        {
            trip.ScheduleRetry(DateTimeOffset.UtcNow, Random.Shared.NextDouble());
        }
        else
        {
            trip.Move(TripStatus.Failed, "Matching failed. Please create a new trip.");
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
                    trip.DriverUnavailable("Driver reservation is no longer available.");
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
                    await WorkflowEvents.Release(trip, bus, ct);
                    trip.DriverUnavailable("Driver offer expired before confirmation.");
                }
                break;
            }
            case TripStatus.AwaitingDriverAcceptance when trip.Deadline <= DateTimeOffset.UtcNow:
                await WorkflowEvents.Release(trip, bus, ct);
                trip.RejectDriver();
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
