namespace SwiftRide.Payments.Infrastructure;

public static class PaymentDatabaseInitializer
{
    public static async Task InitializePaymentDatabase(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await db.Database.MigrateAsync();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (config.GetValue<bool>("Fixtures:Enabled"))
        {
            var funding = scope.ServiceProvider.GetRequiredService<IWalletFunding>();
            for (var i = 1; i <= 100; i++)
            {
                await funding.TopUp(
                    500_000,
                    "seed-opening-balance-v1",
                    new Actor($"rider{i:000}", IsRider: true),
                    CancellationToken.None
                );
            }
        }
    }
}
