namespace SwiftRide.Payments.Application;

public sealed class CashMethod : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Cod;

    public string DebitAccount(string subject) => $"CashHeldByDriver:{subject}";

    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new CaptureResult(CaptureOutcome.Pending));

    // ponytail: records cash returned outside the system; add provider calls for online refunds.
    public Task Refund(Payment payment, CancellationToken ct) => Task.CompletedTask;
}
