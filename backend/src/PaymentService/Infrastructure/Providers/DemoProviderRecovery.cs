namespace SwiftRide.Payments.Infrastructure;

public sealed class DemoProviderRecovery(PayDb db, IHostEnvironment environment) : IProviderRecovery
{
    public async Task<bool> Recover(Guid tripId, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var key = $"trip:{tripId}:charge";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))",
            ct
        );
        var op = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (op is null)
            return false;
        op.Recovered = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
