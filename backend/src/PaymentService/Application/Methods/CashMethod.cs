namespace SwiftRide.Payments.Application;

public sealed record PaymentSimulationSettings(string Outcome = "Pending");

public sealed class CashMethod(PaymentSimulationSettings? simulation = null) : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Cod;

    public string DebitAccount(string subject) => $"CashHeldByDriver:{subject}";

    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
        simulation?.Outcome switch
        {
            "Declined" => Task.FromResult(
                new CaptureResult(
                    CaptureOutcome.Declined,
                    "Local fixture declined the simulated COD payment."
                )
            ),
            "Timeout" => throw new TimeoutException("Local COD payment timeout fixture."),
            null or "Pending" => Task.FromResult(new CaptureResult(CaptureOutcome.Pending)),
            _ => throw new InvalidOperationException("Unsupported local payment fixture."),
        };

    // COD refunds are recorded internally; no external payment provider is called.
    public Task Refund(Payment payment, CancellationToken ct) => Task.CompletedTask;
}
