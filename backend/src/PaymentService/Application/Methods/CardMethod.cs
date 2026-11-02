using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class CardMethod(ICardProvider provider) : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Card;

    public string DebitAccount(string riderId) => "ProviderReceivable";

    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
        provider.Capture(request, ct);

    public Task Refund(Payment payment, CancellationToken ct) => provider.Refund(payment, ct);
}
