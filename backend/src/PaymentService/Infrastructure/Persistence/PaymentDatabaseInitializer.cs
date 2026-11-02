namespace SwiftRide.Payments.Infrastructure;

public static class PaymentDatabaseInitializer
{
    public static async Task InitializePaymentDatabase(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await db.Database.MigrateAsync();
    }
}
