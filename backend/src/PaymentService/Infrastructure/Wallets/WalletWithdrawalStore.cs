using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Infrastructure.Wallets;

public sealed class WalletWithdrawalStore(PayDb db) : IWalletWithdrawalStore
{
    public async Task<WalletWithdrawal?> Find(Guid id, CancellationToken ct)
    {
        var withdrawal = await db.WalletWithdrawals.FindAsync([id], ct);
        if (withdrawal is not null)
            await db.Entry(withdrawal).ReloadAsync(ct);
        return withdrawal;
    }

    public Task<WalletWithdrawal?> FindByKey(string owner, string key, CancellationToken ct) =>
        db.WalletWithdrawals.SingleOrDefaultAsync(
            x => x.OwnerId == owner && x.RequestKey == key,
            ct
        );

    public Task<PageResult<WalletWithdrawal>> History(
        string owner,
        int page,
        CancellationToken ct
    ) => Page(db.WalletWithdrawals.AsNoTracking().Where(x => x.OwnerId == owner), page, ct);

    public Task<WalletWithdrawal?> NextPending(CancellationToken ct) =>
        db
            .WalletWithdrawals.AsNoTracking()
            .Where(x => x.State == "Pending")
            .OrderBy(x => x.RequestedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public void Add(WalletWithdrawal withdrawal) => db.WalletWithdrawals.Add(withdrawal);

    private static async Task<PageResult<WalletWithdrawal>> Page(
        IQueryable<WalletWithdrawal> query,
        int page,
        CancellationToken ct
    )
    {
        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * 10)
            .Take(10)
            .ToListAsync(ct);
        return new(items, page, 10, total);
    }
}
