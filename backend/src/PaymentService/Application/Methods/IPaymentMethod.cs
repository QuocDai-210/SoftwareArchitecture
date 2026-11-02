namespace SwiftRide.Payments.Application;

public enum CaptureOutcome
{
    Settled,
    Pending,
    Declined,
}

public sealed record CaptureResult(
    CaptureOutcome Outcome,
    string? Reason = null,
    bool RequiresCompensation = false,
    string? CheckoutReference = null
)
{
    public bool Success => Outcome == CaptureOutcome.Settled;
}

public interface IPaymentMethod
{
    PaymentMethod Method { get; }
    string DebitAccount(string riderId);
    Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct);
    Task Refund(Payment payment, CancellationToken ct);
}
