namespace SwiftRide.Payments.Application;

public sealed class CardMethod : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Card;

    public string DebitAccount(string riderId) => "CardClearing";

    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
        request.PaymentToken switch
        {
            "card-ok" => Task.FromResult(new CaptureResult(CaptureOutcome.Settled)),
            "card-decline" => Task.FromResult(
                new CaptureResult(CaptureOutcome.Declined, "Test card declined.")
            ),
            "card-timeout" when request.Attempt == 1 => throw new TimeoutException(
                "Test processor unavailable."
            ),
            "card-timeout" => Task.FromResult(new CaptureResult(CaptureOutcome.Settled)),
            _ => throw new RuleException("Unknown test card.", ErrorKind.Validation),
        };

    public Task Refund(Payment payment, CancellationToken ct) => Task.CompletedTask;
}
