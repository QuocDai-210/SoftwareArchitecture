using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class HostedCardProvider(ICheckoutStore store) : ICardProvider
{
    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        if (request.PaymentToken is not null) throw new RuleException("VNPAY không nhận token mô phỏng.", ErrorKind.Validation);
        if (request.Amount == 0) return Task.FromResult(new CaptureResult(true));
        var attempt = new CheckoutAttempt { PaymentId = request.TripId, Amount = request.Amount };
        store.Add(attempt);
        return Task.FromResult(new CaptureResult(false, CheckoutReference: attempt.Reference));
    }

    public Task Refund(Payment payment, CancellationToken ct) =>
        throw new RuleException("Hoàn VNPAY phải được đưa vào hàng chờ đối soát.");
}
