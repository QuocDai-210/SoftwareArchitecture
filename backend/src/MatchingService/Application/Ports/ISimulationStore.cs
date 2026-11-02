namespace SwiftRide.Matching.Application.Ports;

public interface ISimulationStore
{
    Task<Simulation?> Get(string driverId, CancellationToken ct);
    Task<IReadOnlyList<Simulation>> Running(CancellationToken ct);
    Task Save(Simulation simulation, long? expectedRevision, CancellationToken ct);
    Task Position(Simulation simulation, Point position, CancellationToken ct);
    Task<bool> Owns(string driverId, Guid tripId, Guid reservationId, CancellationToken ct);
}
