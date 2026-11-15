namespace SwiftRide.Payments.Infrastructure;

public sealed class PaymentRepository(PayDb db) : IPaymentReader, IPaymentWriter
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

    public void Add(Payment payment) => db.Payments.Add(payment);

    public void Add(Journal journal) => db.Journals.Add(journal);

    public async Task Lock(Guid tripId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({tripId.ToString()}, 0))",
            ct
        );
}
