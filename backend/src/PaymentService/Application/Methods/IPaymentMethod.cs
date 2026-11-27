namespace SwiftRide.Payments.Application;

public enum CaptureOutcome
{
    Settled,
    Declined,
}

public sealed record CaptureResult(CaptureOutcome Outcome, string? Reason = null)
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
