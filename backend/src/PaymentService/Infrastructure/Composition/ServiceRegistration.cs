using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;
using SwiftRide.Application.Persistence;

namespace SwiftRide.Payments.Infrastructure;

public static class ServiceRegistration
{
    public static void AddPaymentInfrastructure(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("OTEL_SDK_DISABLED"))
            builder.Services.AddHostedService<PaymentOutcomeMetrics>();
        builder.Services.AddScoped<IWalletAccounts, WalletAccounts>();
        builder.Services.AddDbContext<PayDb>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Payment"))
        );
        builder.Services.AddScoped<PaymentRepository>();
        builder.Services.AddScoped<IPaymentReader>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IPaymentWriter>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork<PayDb>>();
        builder.Services.AddScoped<IPaymentMethod, CardMethod>();
        builder.Services.AddScoped<IPaymentMethod, WalletMethod>();
        builder.Services.AddScoped<PaymentMethodFactory>();
        builder.Services.AddScoped<PaymentCommands>();
        builder.Services.AddScoped<PaymentQueries>();
        builder.Services.AddScoped<IReadinessProbe, EfReadiness<PayDb>>();
        builder.AddReliableBus<PayDb>(x =>
        {
            x.AddConsumer<RideCompletedConsumer>();
            x.AddConsumer<RefundConsumer>();
        });
    }
}
