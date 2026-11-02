namespace SwiftRide.Application.Observability;

public interface ITripMetrics
{
    void Requested();
    void Outcome(string status);
    IDisposable? TraceWorkflow(Guid tripId, string correlationId);
}
