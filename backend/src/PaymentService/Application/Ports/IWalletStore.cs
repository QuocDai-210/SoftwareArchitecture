namespace SwiftRide.Payments.Application.Ports;

public interface IWalletStore
{
    Task<Wallet?> GetLocked(string riderId, CancellationToken ct);
    Task<Wallet> GetOrCreateLocked(string riderId, CancellationToken ct);
}
