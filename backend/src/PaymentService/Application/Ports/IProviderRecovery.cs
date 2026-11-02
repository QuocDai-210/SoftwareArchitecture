namespace SwiftRide.Payments.Application.Ports;

public interface IProviderRecovery
{
    Task<bool> Recover(Guid tripId, CancellationToken ct);
}
