namespace SwiftRide.Payments.Infrastructure;

public sealed class PaymentRepository(PayDb db) : IPaymentReader, IPaymentWriter
{
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
