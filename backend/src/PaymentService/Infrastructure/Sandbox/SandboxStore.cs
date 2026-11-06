using SwiftRide.Payments.Application.Sandbox;

namespace SwiftRide.Payments.Infrastructure.Sandbox;

public sealed class SandboxStore(PayDb db) : ISandboxStore
{
    public async Task Lock(string key, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct
        );
    }

    public async Task<SandboxPayment?> Find(string reference, CancellationToken ct)
    {
        var payment = await db.SandboxPayments.FindAsync([reference], ct);
        if (payment is not null)
        {
            // Refresh tracked state after acquiring the lock, including during reconciliation.
            await db.Entry(payment).ReloadAsync(ct);
        }
        return payment;
    }

    public Task<SandboxPayment?> FindRequest(string owner, string key, CancellationToken ct) =>
        db.SandboxPayments.SingleOrDefaultAsync(p => p.OwnerId == owner && p.RequestKey == key, ct);

    public async Task<IReadOnlyList<string>> Due(DateTimeOffset now, CancellationToken ct) =>
        await db
            .SandboxPayments.AsNoTracking()
            .Where(p =>
                (p.State == "Pending" || p.State == "NeedsReview")
                && p.CheckCount < 12
                && p.NextCheckAt <= now
                && (p.NextQueryAt == null || p.NextQueryAt <= now)
            )
            .OrderBy(p => p.NextCheckAt)
            .Select(p => p.Reference)
            .Take(20)
            .ToListAsync(ct);

    public void Add(SandboxPayment payment) => db.SandboxPayments.Add(payment);
}
