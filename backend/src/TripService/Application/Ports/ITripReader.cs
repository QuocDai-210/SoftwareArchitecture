namespace SwiftRide.Trips.Application.Ports;

public interface ITripReader
{
    Task<Trip> Get(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Trip>> List(string subject, bool all, CancellationToken ct);
    Task<IReadOnlyList<Trip>> Offers(string driverId, CancellationToken ct);
    Task<PageResult<TripView>> History(
        string subject,
        bool all,
        int page,
        string? status,
        CancellationToken ct
    );
    Task<Trip?> ByBookingKey(string riderId, string key, CancellationToken ct);
}
