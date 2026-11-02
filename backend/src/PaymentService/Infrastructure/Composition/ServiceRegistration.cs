using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;
using SwiftRide.Application.Persistence;

namespace SwiftRide.Payments.Infrastructure;

public static class ServiceRegistration
{
    public static void AddPaymentInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<PayDb>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Payment"))
        );
        builder.Services.AddScoped<PaymentRepository>();
        builder.Services.AddScoped<IPaymentReader>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IPaymentWriter>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IWalletStore>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork<PayDb>>();
        builder.Services.AddScoped<ICheckoutStore, CheckoutStore>();
        builder.Services.AddScoped<IVnpayGateway, VnpayGateway>();
        builder.Services.AddHttpClient("vnpay", client =>
        {
            client.BaseAddress = new Uri("https://sandbox.vnpayment.vn/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers();
        if (builder.Configuration["Payments:Provider"] == "Test")
            builder.Services.AddScoped<ICardProvider, DemoCardProvider>();
        else
            builder.Services.AddScoped<ICardProvider, HostedCardProvider>();
        builder.Services.AddScoped<HostedPayments>();
        builder.Services.AddHostedService<PaymentReconciliationWorker>();
        builder.Services.AddScoped<IProviderRecovery, DemoProviderRecovery>();
        builder.Services.AddScoped<IPaymentMethod, CardMethod>();
        builder.Services.AddScoped<IPaymentMethod, WalletMethod>();
        builder.Services.AddScoped<PaymentMethodFactory>();
        builder.Services.AddScoped<PaymentCommands>();
        builder.Services.AddScoped<PaymentQueries>();
        builder.Services.AddScoped<RecoverProvider>();
        builder.Services.AddScoped<IReadinessProbe, EfReadiness<PayDb>>();
        builder.AddReliableBus<PayDb>(true, x => x.AddConsumer<RideCompletedConsumer>());
    }
}
