namespace SwiftRide.Matching.Application.Ports;

public interface IDriverEligibility
{
    Task<bool> Approved(string driverId, CancellationToken ct);
}
