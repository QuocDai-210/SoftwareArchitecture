namespace SwiftRide.Matching.Application.Ports;

public interface IDriverReader
{
    Task<Driver?> Get(string driverId, CancellationToken ct);
    Task<DriverMatch?> Nearest(Point pickup, string[] excluded, CancellationToken ct);
}
