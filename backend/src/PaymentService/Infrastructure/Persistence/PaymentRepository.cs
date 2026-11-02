namespace SwiftRide.Payments.Infrastructure;

public sealed class PaymentRepository(PayDb db) : IPaymentReader, IPaymentWriter, IWalletStore
{
    public async Task<PageResult<PaymentView>> History(
        string subject,
        bool all,
        int page,
        CancellationToken ct
    )
    {
        var query = db
            .Payments.AsNoTracking()
            .Where(x => all || x.RiderId == subject || x.DriverId == subject);
        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * 10)
            .Take(10)
            .ToListAsync(ct);
        return new(items.Select(PaymentView.From).ToArray(), page, 10, count);
    }

    public async Task<object> Statistics(string subject, bool all, CancellationToken ct) =>
        await db
            .Payments.AsNoTracking()
            .Where(x => all || x.RiderId == subject || x.DriverId == subject)
            .GroupBy(x => new
            {
                x.Status,
                x.IsTopUp,
                x.Method,
            })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.IsTopUp,
                g.Key.Method,
                Count = g.Count(),
                Amount = g.Sum(x => x.Amount),
            })
            .ToListAsync(ct);

    public Task<Payment?> Find(Guid id, CancellationToken ct) =>
        db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Payment?> FindTracked(Guid id, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Journal>> Journals(Guid paymentId, CancellationToken ct) =>
        await db
            .Journals.AsNoTracking()
            .Where(x => x.PaymentId == paymentId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<decimal> Balance(string riderId, CancellationToken ct) =>
        await db
            .Wallets.AsNoTracking()
            .Where(x => x.RiderId == riderId)
            .Select(x => (decimal?)x.Balance)
            .SingleOrDefaultAsync(ct)
        ?? 0;

    public Task<Payment?> ByKey(string key, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);

    public async Task<Wallet> GetOrCreateLocked(string riderId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"wallet:" + riderId}, 0))",
            ct
        );
        var wallet = await GetLocked(riderId, ct);
        if (wallet is not null)
            return wallet;
        wallet = Wallet.Funded(riderId, 0);
        db.Wallets.Add(wallet);
        return wallet;
    }

    public void Add(Payment payment) => db.Payments.Add(payment);

    public void Add(Journal journal) => db.Journals.Add(journal);

    public async Task Lock(Guid tripId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({tripId.ToString()}, 0))",
            ct
        );

    public Task<Wallet?> GetLocked(string riderId, CancellationToken ct) =>
        db
            .Wallets.FromSqlInterpolated(
                $"SELECT * FROM \"Wallets\" WHERE \"RiderId\" = {riderId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
}
