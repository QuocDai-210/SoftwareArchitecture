namespace SwiftRide.Matching.Application.Ports;

public interface IDriverReader
{
    Task<DriverContact?> Contact(string driverId, CancellationToken ct);
    Task<Driver?> Get(string driverId, CancellationToken ct);
    Task<DriverMatch?> Nearest(Point pickup, string[] excluded, CancellationToken ct);
}
