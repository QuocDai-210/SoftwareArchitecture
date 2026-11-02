namespace SwiftRide.Trips.Application.Ports;

public interface ITripWriter
{
    Task<Trip> Create(Trip trip, CancellationToken ct);
    Task Save(CancellationToken ct);
}
