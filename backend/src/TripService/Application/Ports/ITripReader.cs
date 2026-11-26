namespace SwiftRide.Trips.Application.Ports;

public interface ITripReader
{
    Task<Trip> Get(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Trip>> List(string subject, bool all, CancellationToken ct);
    Task<IReadOnlyList<Trip>> Offers(string driverId, CancellationToken ct);
    Task<Trip?> ByBookingKey(string riderId, string key, CancellationToken ct);
}
