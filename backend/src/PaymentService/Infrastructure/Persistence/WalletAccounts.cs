namespace SwiftRide.Payments.Infrastructure;

public sealed class WalletAccounts(PayDb db) : IWalletAccounts
{
    public async Task<long> Balance(string owner, CancellationToken ct) =>
        await db
            .WalletAccounts.Where(w => w.OwnerId == owner)
            .Select(w => (long?)w.Available)
            .SingleOrDefaultAsync(ct)
        ?? 0;

    public async Task<bool> Debit(string owner, long amount, CancellationToken ct) =>
        await db
            .WalletAccounts.Where(w => w.OwnerId == owner && w.Available >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Available, w => w.Available - amount), ct)
        == 1;

    public async Task Credit(string owner, long amount, CancellationToken ct)
    {
        var updated = await db
            .WalletAccounts.Where(w => w.OwnerId == owner)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.Available, w => w.Available + amount),
                ct
            );
        if (updated != 1)
            throw new RuleException("Missing wallet for refund.");
    }
}
