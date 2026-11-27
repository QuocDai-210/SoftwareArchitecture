namespace SwiftRide.Payments.Infrastructure;

public static class PaymentDatabaseInitializer
{
    public static async Task InitializePaymentDatabase(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "WalletAccounts" ("OwnerId", "Available") VALUES
                ('11111111-1111-1111-1111-000000000001', 500000),
                ('11111111-1111-1111-1111-000000000002', 0)
            ON CONFLICT ("OwnerId") DO NOTHING
            """
        );
    }
}
