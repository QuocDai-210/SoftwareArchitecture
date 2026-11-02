namespace SwiftRide.Payments.Infrastructure;

public sealed class CheckoutStore(PayDb db) : ICheckoutStore
{
    public void Add(CheckoutAttempt attempt) => db.CheckoutAttempts.Add(attempt);

    public async Task<CheckoutAttempt?> Find(string reference, CancellationToken ct)
    {
        var attempt = await db.CheckoutAttempts.SingleOrDefaultAsync(
            x => x.Reference == reference,
            ct
        );
        if (attempt is not null)
            await db.Entry(attempt).ReloadAsync(ct);
        return attempt;
    }

    public async Task<IReadOnlyList<Guid>> Pending(CancellationToken ct) =>
        await db
            .Payments.AsNoTracking()
            .Where(x =>
                x.CheckoutReference != null
                && (x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.RefundPending)
            )
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(ct);
}
