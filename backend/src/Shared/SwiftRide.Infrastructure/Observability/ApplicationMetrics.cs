using System.Diagnostics;
using SwiftRide.Application.Observability;

namespace SwiftRide.Infrastructure;

public sealed class ApplicationMetrics : ITripMetrics, IMatchingMetrics
{
    public void Requested() => Telemetry.Trips.Add(1);

    void ITripMetrics.Outcome(string status) =>
        Telemetry.Outcomes.Add(1, new KeyValuePair<string, object?>("status", status));

    public void Duration(double seconds) => Telemetry.Matching.Record(seconds);

    public void PaymentOutcome(string status) =>
        Telemetry.PaymentAttempts.Add(1, new KeyValuePair<string, object?>("status", status));

    public IDisposable? TraceWorkflow(Guid tripId, string correlationId, string? traceParent)
    {
        ActivityContext.TryParse(traceParent, null, out var parent);
        var activity = Telemetry.Activities.StartActivity(
            "trip.workflow",
            ActivityKind.Internal,
            parent
        );
        activity?.SetTag("trip.id", tripId);
        activity?.SetTag("correlation.id", correlationId);
        return activity;
    }
}
