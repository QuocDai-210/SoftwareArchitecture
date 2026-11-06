using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Infrastructure.Wallets;

public sealed class WalletAccounts(PayDb db) : IWalletAccounts
{
    public async Task Lock(string owner, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"wallet-account:" + owner}, 0))",
            ct
        );
    }

    public async Task<WalletAccount?> Find(string owner, CancellationToken ct)
    {
        var wallet = await db.WalletAccounts.FindAsync([owner], ct);
        if (wallet is not null)
        {
            await db.Entry(wallet).ReloadAsync(ct);
        }
        return wallet;
    }

    public Task<WalletActivity?> FindActivity(string reference, CancellationToken ct) =>
        db.WalletActivities.AsNoTracking().SingleOrDefaultAsync(p => p.Reference == reference, ct);

    public async Task<PageResult<WalletActivity>> History(
        string owner,
        int page,
        CancellationToken ct
    )
    {
        var query = db.WalletActivities.AsNoTracking().Where(p => p.OwnerId == owner);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Reference)
            .Skip((page - 1) * 10)
            .Take(10)
            .ToListAsync(ct);
        return new(items, page, 10, total);
    }

    public async Task<PageResult<SandboxView>> TopUps(string owner, int page, CancellationToken ct)
    {
        var query = db
            .SandboxPayments.AsNoTracking()
            .Where(p => p.OwnerId == owner && p.Purpose == "WalletTopUp");
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Reference)
            .Skip((page - 1) * 10)
            .Take(10)
            .Select(p => new SandboxView(
                p.Reference,
                p.Amount,
                p.State,
                p.CreatedAt,
                p.ExpiresAt,
                p.ConfirmedAt,
                null,
                p.NextQueryAt,
                p.Purpose,
                p.WalletCreditedAt
            ))
            .ToListAsync(ct);
        return new(items, page, 10, total);
    }

    public void Add(WalletAccount account) => db.WalletAccounts.Add(account);

    public void Add(WalletActivity activity) => db.WalletActivities.Add(activity);

    public void Add(Journal journal) => db.Journals.Add(journal);

    public Task HoldTrip(Guid tripId, string riderId, long amount, CancellationToken ct) =>
        HoldTripCore(tripId, riderId, amount, ct);

    private async Task HoldTripCore(Guid tripId, string riderId, long amount, CancellationToken ct)
    {
        await Lock(riderId, ct);
        var reference = $"trip:{tripId:N}:hold";
        var existing = await FindActivity(reference, ct);
        if (existing is not null)
        {
            if (existing.OwnerId != riderId || existing.Amount != amount)
                throw new RuleException("Khoản giữ ví không khớp chuyến.");
            return;
        }
        var wallet =
            await Find(riderId, ct)
            ?? throw new RuleException("Ví chưa có số dư.", ErrorKind.Validation);
        wallet.Hold(amount);
        AddTripActivity(
            reference,
            riderId,
            amount,
            wallet,
            "TripHold",
            tripId,
            new($"Wallet:{riderId}:Available", EntrySide.Debit, amount),
            new($"Wallet:{riderId}:Held", EntrySide.Credit, amount)
        );
    }

    public async Task ReleaseTrip(Guid tripId, string riderId, long amount, CancellationToken ct)
    {
        await Lock(riderId, ct);
        var reference = $"trip:{tripId:N}:release";
        if (await FindActivity(reference, ct) is not null)
            return;
        var hold = await FindActivity($"trip:{tripId:N}:hold", ct);
        if (hold is null)
            return;
        if (hold.OwnerId != riderId || hold.Amount != amount)
            throw new RuleException("Không tìm thấy khoản giữ ví cần hoàn.");
        if (await FindActivity($"trip:{tripId:N}:settle", ct) is not null)
            throw new RuleException("Khoản giữ đã được thanh toán.");
        var wallet =
            await Find(riderId, ct)
            ?? throw new RuleException("Không tìm thấy ví.", ErrorKind.NotFound);
        wallet.Release(amount);
        AddTripActivity(
            reference,
            riderId,
            amount,
            wallet,
            "TripHoldReleased",
            tripId,
            new($"Wallet:{riderId}:Held", EntrySide.Debit, amount),
            new($"Wallet:{riderId}:Available", EntrySide.Credit, amount)
        );
    }

    public async Task SettleTrip(
        Guid tripId,
        string riderId,
        string driverId,
        long amount,
        CancellationToken ct
    )
    {
        foreach (var owner in new[] { riderId, driverId }.Distinct().Order())
            await Lock(owner, ct);
        var reference = $"trip:{tripId:N}:settle";
        if (await FindActivity(reference, ct) is not null)
            return;
        if (await FindActivity($"trip:{tripId:N}:release", ct) is not null)
            throw new RuleException("Khoản giữ đã được hoàn.");
        var hold = await FindActivity($"trip:{tripId:N}:hold", ct);
        if (hold is null || hold.OwnerId != riderId || hold.Amount != amount)
            throw new RuleException("Khoản giữ không khớp chuyến.");
        var wallet =
            await Find(riderId, ct)
            ?? throw new RuleException("Không tìm thấy ví.", ErrorKind.NotFound);
        var earningReference = $"trip:{tripId:N}:earning";
        if (await FindActivity(earningReference, ct) is not null)
            return;
        wallet.Settle(amount);
        var driverWallet = await Find(driverId, ct);
        if (driverWallet is null)
        {
            driverWallet = new WalletAccount { OwnerId = driverId };
            Add(driverWallet);
        }
        driverWallet.CreditEarnings(amount);
        AddTripActivity(
            reference,
            riderId,
            amount,
            wallet,
            "TripSettled",
            tripId,
            new($"Wallet:{riderId}:Held", EntrySide.Debit, amount),
            new("TripPayable", EntrySide.Credit, amount)
        );
        AddTripActivity(
            earningReference,
            driverId,
            amount,
            driverWallet,
            "DriverEarning",
            tripId,
            new("TripPayable", EntrySide.Debit, amount),
            new($"Wallet:{driverId}:Available", EntrySide.Credit, amount)
        );
    }

    private void AddTripActivity(
        string reference,
        string owner,
        long amount,
        WalletAccount wallet,
        string kind,
        Guid tripId,
        params LedgerLine[] lines
    )
    {
        var journal = new Journal
        {
            PaymentId = tripId,
            Kind = kind,
            Lines = [.. lines],
        };
        Ledger.Validate(journal.Lines);
        Add(journal);
        Add(
            new WalletActivity
            {
                Reference = reference,
                OwnerId = owner,
                JournalId = journal.Id,
                Kind = kind,
                Amount = amount,
                AvailableAfter = wallet.Available,
                HeldAfter = wallet.Held,
                PendingAfter = wallet.PendingEarnings,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
    }
}
