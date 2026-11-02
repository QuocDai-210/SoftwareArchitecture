namespace SwiftRide.Application.Health;

public interface IReadinessProbe
{
    Task<bool> Ready(CancellationToken ct);
}
