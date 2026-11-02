namespace SwiftRide.Payments.Application.Ports;

public interface ICardProvider
{
    Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct);
    Task Refund(Payment payment, CancellationToken ct);
}
