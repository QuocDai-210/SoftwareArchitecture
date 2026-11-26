namespace SwiftRide.Trips.Application.Ports;

public sealed record DueTrip(Guid Id, long Version);

public interface ITripWorkflowQueue
{
    Task<IReadOnlyList<DueTrip>> Due(DateTimeOffset now, CancellationToken ct);
}
