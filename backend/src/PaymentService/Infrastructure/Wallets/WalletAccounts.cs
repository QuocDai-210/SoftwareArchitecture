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

    public async Task<PageResult<WalletActivity>> TopUps(
        string owner,
        int page,
        CancellationToken ct
    )
    {
        var query = db
            .WalletActivities.AsNoTracking()
            .Where(p => p.OwnerId == owner && p.Kind == "WalletTopUp");
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Reference)
            .Skip((page - 1) * 10)
            .Take(10)
            .ToListAsync(ct);
        return new(items, page, 10, total);
    }

    public void Add(WalletAccount account) => db.WalletAccounts.Add(account);

    public void Add(WalletActivity activity) => db.WalletActivities.Add(activity);

    public void Add(Journal journal) => db.Journals.Add(journal);

    public async Task HoldTrip(
        Guid tripId,
        string riderId,
        long amount,
        CancellationToken ct,
        Guid? quoteId = null
    )
    {
        await Lock(riderId, ct);
        if (await db.WalletHoldClosures.FindAsync([tripId, quoteId ?? Guid.Empty], ct) is not null)
            throw new RuleException("Báo giá đã đóng khoản giữ. Cần xác nhận báo giá mới.");
        if (await FindActivity($"trip:{tripId:N}:settle", ct) is not null)
            throw new RuleException("Chuyến đã được quyết toán.");
        var (reference, existing) = await ResolveHold(tripId, quoteId, ct);
        if (existing is not null)
        {
            if (existing.OwnerId != riderId || existing.Amount != amount)
                throw new RuleException("Khoản giữ ví không khớp chuyến.");
            if (await FindActivity(ReleaseReference(reference), ct) is not null)
                throw new RuleException("Khoản giữ của báo giá đã được trả. Cần báo giá mới.");
            return;
        }
        var wallet = await Find(riderId, ct)
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

    public async Task ReleaseTrip(
        Guid tripId,
        string riderId,
        long amount,
        CancellationToken ct,
        Guid? quoteId = null
    )
    {
        await Lock(riderId, ct);
        var closed = await db.WalletHoldClosures.FindAsync([tripId, quoteId ?? Guid.Empty], ct);
        if (closed is not null)
        {
            if (closed.RiderId != riderId || closed.Amount != amount)
                throw new RuleException("Yêu cầu giải phóng khoản giữ không khớp.");
            return;
        }
        var (holdReference, hold) = await ResolveHold(tripId, quoteId, ct);
        var reference = ReleaseReference(holdReference);
        db.WalletHoldClosures.Add(
            new WalletHoldClosure
            {
                TripId = tripId,
                QuoteId = quoteId ?? Guid.Empty,
                RiderId = riderId,
                Amount = amount,
            }
        );
        if (await FindActivity(reference, ct) is not null)
            return;
        if (hold is null)
            return;
        if (hold.OwnerId != riderId || hold.Amount != amount)
            throw new RuleException("Không tìm thấy khoản giữ ví cần hoàn.");
        if (await FindActivity($"trip:{tripId:N}:settle", ct) is not null)
            throw new RuleException("Khoản giữ đã được thanh toán.");
        var wallet = await Find(riderId, ct)
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
        CancellationToken ct,
        Guid? quoteId = null
    )
    {
        foreach (var owner in new[] { riderId, driverId }.Distinct().Order())
            await Lock(owner, ct);
        var reference = $"trip:{tripId:N}:settle";
        if (await FindActivity(reference, ct) is not null)
            return;
        var (holdReference, hold) = await ResolveHold(tripId, quoteId, ct);
        if (await FindActivity(ReleaseReference(holdReference), ct) is not null)
            throw new RuleException("Khoản giữ đã được hoàn.");
        if (hold is null || hold.OwnerId != riderId || hold.Amount != amount)
            throw new RuleException("Khoản giữ không khớp chuyến.");
        var wallet = await Find(riderId, ct)
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

    private async Task<(string Reference, WalletActivity? Activity)> ResolveHold(
        Guid tripId,
        Guid? quoteId,
        CancellationToken ct
    )
    {
        var legacy = $"trip:{tripId:N}:hold";
        var reference = quoteId is null ? legacy : $"{legacy}:{quoteId:N}";
        var activity = await FindActivity(reference, ct);
        if (activity is not null || quoteId is null)
            return (reference, activity);
        // Preserve active holds created before quote-scoped references were introduced.
        var previous = await FindActivity(legacy, ct);
        if (previous is not null && await FindActivity(ReleaseReference(legacy), ct) is null)
            return (legacy, previous);
        return (reference, null);
    }

    private static string ReleaseReference(string holdReference) =>
        holdReference.Replace(":hold", ":release", StringComparison.Ordinal);

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
