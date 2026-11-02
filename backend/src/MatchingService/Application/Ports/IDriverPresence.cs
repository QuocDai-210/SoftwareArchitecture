namespace SwiftRide.Matching.Application.Ports;

public interface IDriverPresence
{
    Task Heartbeat(string driverId, CancellationToken ct);
    Task Location(string driverId, string name, Point point, CancellationToken ct);
    Task<bool> Availability(string driverId, bool available, CancellationToken ct);
}
