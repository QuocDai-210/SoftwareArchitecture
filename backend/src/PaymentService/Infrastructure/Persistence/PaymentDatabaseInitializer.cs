namespace SwiftRide.Payments.Infrastructure;

public static class PaymentDatabaseInitializer
{
    public static async Task InitializePaymentDatabase(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await db.Database.MigrateAsync();
        foreach (
            var rider in new[]
            {
                "11111111-1111-1111-1111-000000000001",
                "11111111-1111-1111-1111-000000000002",
            }
        )
        {
            if (await db.Wallets.AnyAsync(x => x.RiderId == rider))
                continue;
            db.Wallets.Add(Wallet.Funded(rider, 2000000));
            db.Journals.Add(
                new Journal
                {
                    PaymentId = Guid.Empty,
                    Kind = $"Seed:{rider}",
                    Lines =
                    [
                        new("DemoFunding", EntrySide.Debit, 2000000),
                        new($"WalletLiability:{rider}", EntrySide.Credit, 2000000),
                    ],
                }
            );
            await db.SaveChangesAsync();
        }
    }
}
