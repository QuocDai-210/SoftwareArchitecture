using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripPaymentNotifications(
    ITripReader reader,
    ITripWriter writer,
    ITripMetrics metrics,
    IPaymentMetrics paymentMetrics
)
{
    public async Task Settled(PaymentSettled message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status == TripStatus.Paid)
            return;
        if (
            message.PaymentId != trip.Id
            || message.Currency != "VND"
            || message.Amount != trip.Quote?.Amount
        )
            throw new RuleException("PaymentSettled không khớp chuyến.");
        trip.Move(TripStatus.Paid);
        await writer.Save(ct);
        metrics.Outcome("paid");
    }

    public async Task Failed(PaymentFailed message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending)
            return;
        trip.Move(TripStatus.PaymentReview, message.Reason);
        await writer.Save(ct);
    }

    public async Task Faulted(RideCompleted message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Status != TripStatus.PaymentPending)
            return;
        trip.Move(
            TripStatus.PaymentReview,
            "Thanh toán hết lượt retry; cần đối soát hoặc phát lại."
        );
        paymentMetrics.Outcome("error");
        await writer.Save(ct);
    }
}
