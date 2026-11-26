namespace SwiftRide.Payments.Application.Ports;

public interface IWalletAccounts
{
    Task<long> Balance(string owner, CancellationToken ct);
    Task<bool> Debit(string owner, long amount, CancellationToken ct);
    Task Credit(string owner, long amount, CancellationToken ct);
}
