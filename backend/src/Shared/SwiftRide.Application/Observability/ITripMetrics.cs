namespace SwiftRide.Application.Observability;

public interface ITripMetrics
{
    void Requested();
    void Outcome(string status);
    void PaymentOutcome(string status);
    IDisposable? TraceWorkflow(Guid tripId, string correlationId, string? traceParent);
}
