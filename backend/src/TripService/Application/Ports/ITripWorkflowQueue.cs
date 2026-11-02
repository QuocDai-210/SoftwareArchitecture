namespace SwiftRide.Trips.Application.Ports;

public interface ITripWorkflowQueue
{
    Task<IReadOnlyList<Guid>> Due(DateTimeOffset now, CancellationToken ct);
}
