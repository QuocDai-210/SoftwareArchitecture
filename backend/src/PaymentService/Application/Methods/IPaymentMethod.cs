namespace SwiftRide.Payments.Application;

public sealed record CaptureResult(
    bool Success,
    string? Reason = null,
    bool RequiresCompensation = false,
    string? CheckoutReference = null
);

public interface IPaymentMethod
{
    PaymentMethod Method { get; }
    string DebitAccount(string riderId);
    Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct);
    Task Refund(Payment payment, CancellationToken ct);
}
