using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripPaymentNotifications(
    ITripReader reader,
    ITripWriter writer,
    ITripMetrics metrics,
    IPaymentMetrics paymentMetrics,
    IEventPublisher bus
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
            || (
                trip.Method == SwiftRide.SharedKernel.PaymentMethod.Wallet
                && message.DriverId != trip.DriverId
            )
        )
            throw new RuleException("PaymentSettled không khớp chuyến.");
        trip.Move(TripStatus.Paid);
        await WorkflowEvents.Release(trip, bus, ct);
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

    public async Task WalletHoldSucceeded(WalletTripHoldSucceeded message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (trip.Quote?.Id != message.QuoteId)
            return;
        if (
            trip.Method != SwiftRide.SharedKernel.PaymentMethod.Wallet
            || trip.RiderId != message.RiderId
            || trip.Quote?.Amount != message.Amount
        )
            throw new RuleException("Khoản giữ ví không khớp chuyến.");
        if (trip.Status != TripStatus.WalletHoldPending)
            return;
        var quote = trip.Quote;
        if (
            quote is null
            || quote.Id != message.QuoteId
            || quote.ExpiresAt <= DateTimeOffset.UtcNow
        )
        {
            trip.WalletHoldFailed(
                "Báo giá đã hết hạn trong lúc giữ tiền. Xác nhận lại giá để tiếp tục."
            );
            await bus.Publish(
                WorkflowEvents.ReleaseWallet(trip, rematch: true),
                trip.CorrelationId,
                ct
            );
            await writer.Save(ct);
            return;
        }
        trip.WalletHoldSucceeded(message.QuoteId, DateTimeOffset.UtcNow);
        await writer.Save(ct);
    }

    public async Task WalletHoldFailed(WalletTripHoldFailed message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (
            trip.Method != SwiftRide.SharedKernel.PaymentMethod.Wallet
            || trip.RiderId != message.RiderId
            || trip.Quote?.Amount != message.Amount
            || (message.QuoteId is not null && trip.Quote?.Id != message.QuoteId)
            || trip.Status != TripStatus.WalletHoldPending
        )
            return;
        trip.WalletHoldFailed(message.Reason);
        await bus.Publish(WorkflowEvents.ReleaseWallet(trip), trip.CorrelationId, ct);
        await writer.Save(ct);
    }

    public async Task WalletHoldReleased(WalletTripHoldReleased message, CancellationToken ct)
    {
        var trip = await reader.Get(message.TripId, ct);
        if (message.QuoteId is not null && trip.Quote?.Id != message.QuoteId)
            return;
        if (trip.RiderId != message.RiderId || trip.Quote?.Amount != message.Amount)
            throw new RuleException("Khoản giữ hoàn lại không khớp chuyến.");
        if (trip.Status != TripStatus.PaymentReview)
            return;
        if (message.CancelRequested)
        {
            trip.WalletHoldCancelled();
            await WorkflowEvents.Release(trip, bus, ct);
        }
        else if (message.Rematch)
            trip.WalletHoldReleasedForRematch();
        else
            trip.WalletHoldReleased();
        await writer.Save(ct);
    }
}
