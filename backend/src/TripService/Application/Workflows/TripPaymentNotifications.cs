using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripPaymentNotifications(
    ITripReader reader,
    ITripWriter writer,
    ITripMetrics metrics,
    IEventPublisher bus
)
{
    public async Task Settled(PaymentSettled message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (
            message.PaymentId != trip.Id
            || message.Currency != "VND"
            || message.DriverId != trip.DriverId
        )
            throw new RuleException("Settlement identity or currency mismatch.");
        if (
            trip.Status is not (TripStatus.PaymentPending or TripStatus.PaymentReview)
            || trip.Refunded
        )
            return;
        if (message.Amount != trip.Quote?.Amount)
        {
            if (trip.Status == TripStatus.PaymentPending)
                trip.Move(TripStatus.PaymentReview, "Settlement amount mismatch.");
            await bus.Publish(
                new RefundRequested(trip.Id, message.Amount, trip.CorrelationId),
                trip.CorrelationId,
                ct
            );
            await writer.Save(ct);
            return;
        }
        trip.Move(TripStatus.Paid);
        await writer.Save(ct);
        metrics.Outcome("paid");
    }

    public async Task Failed(PaymentFailed message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending || message.Attempt != trip.PaymentAttempt)
            return;
        trip.Move(TripStatus.PaymentReview, message.Reason);
        await writer.Save(ct);
    }

    public async Task Faulted(RideCompleted message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending || message.Attempt != trip.PaymentAttempt)
            return;
        trip.Move(
            TripStatus.PaymentReview,
            "Thanh toán hết lượt retry; cần đối soát hoặc phát lại."
        );
        await writer.Save(ct);
    }

    public async Task Refunded(PaymentRefunded message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (message.PaymentId != trip.Id || trip.Refunded)
            return;
        trip.RecordRefund();
        await writer.Save(ct);
    }
}
