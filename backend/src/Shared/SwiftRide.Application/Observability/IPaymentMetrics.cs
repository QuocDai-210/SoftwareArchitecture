namespace SwiftRide.Application.Observability;

public interface IPaymentMetrics
{
    void Outcome(string status);
}
