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
            trip.Status is not (TripStatus.PaymentPending or TripStatus.PaymentReview)
            || trip.Refunded
        )
            return;
        if (
            message.PaymentId != trip.Id
            || message.Currency != "VND"
            || message.DriverId != trip.DriverId
            || message.RiderId is null
            || message.Method is null
        )
        {
            // Older settlement events omit payer and method. A replay obtains the current receipt.
            await Review(trip, "Settlement identity or currency requires reconciliation.", ct);
            return;
        }
        if (
            message.Amount != trip.Quote?.Amount
            || message.RiderId != trip.RiderId
            || message.Method != trip.Method
        )
        {
            var pending = trip.Status == TripStatus.PaymentPending;
            if (pending)
                trip.Move(TripStatus.PaymentReview, "Charge differs from the confirmed trip.");
            await bus.Publish(
                new RefundRequested(trip.Id, message.Amount, trip.CorrelationId),
                trip.CorrelationId,
                ct
            );
            await writer.Save(ct);
            if (pending)
                metrics.PaymentOutcome("mismatch");
            return;
        }
        trip.Move(TripStatus.Paid);
        await writer.Save(ct);
        metrics.Outcome("paid");
        metrics.PaymentOutcome("settled");
    }

    public async Task Failed(PaymentFailed message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending || message.Attempt != trip.PaymentAttempt)
            return;
        await Review(trip, message.Reason, ct, "declined");
    }

    public async Task Faulted(RideCompleted message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending || message.Attempt != trip.PaymentAttempt)
            return;
        await Review(trip, "Thanh toán hết lượt retry; cần đối soát hoặc phát lại.", ct, "faulted");
    }

    private async Task Review(
        Trip trip,
        string reason,
        CancellationToken ct,
        string outcome = "mismatch"
    )
    {
        if (trip.Status != TripStatus.PaymentPending)
            return;
        trip.Move(TripStatus.PaymentReview, reason);
        await writer.Save(ct);
        metrics.PaymentOutcome(outcome);
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
