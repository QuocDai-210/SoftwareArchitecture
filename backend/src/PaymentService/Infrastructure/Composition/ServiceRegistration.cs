using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;
using SwiftRide.Application.Persistence;
using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Infrastructure.Sandbox;
using SwiftRide.Payments.Infrastructure.Wallets;

namespace SwiftRide.Payments.Infrastructure;

public static class ServiceRegistration
{
    public static void AddPaymentInfrastructure(this WebApplicationBuilder builder)
    {
        var sandbox = new SandboxVnpayOptions();
        builder.Configuration.GetSection("VnpaySandbox").Bind(sandbox);
        builder.Services.AddSingleton(sandbox);
        builder.Services.AddSingleton(TimeProvider.System);
        builder
            .Services.AddHttpClient<ISandboxVnpay, SandboxVnpay>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.MaxResponseContentBufferSize = 65536;
            })
            .RemoveAllLoggers();
        builder.Services.AddScoped<ISandboxStore, SandboxStore>();
        builder.Services.AddScoped<SandboxPayments>();
        builder.Services.AddScoped<IWalletAccounts, WalletAccounts>();
        builder.Services.AddScoped<IWalletWithdrawalStore, WalletWithdrawalStore>();
        builder.Services.AddScoped<IWalletFunding, WalletFunding>();
        builder.Services.AddScoped<WalletQueries>();
        builder.Services.AddScoped<WalletWithdrawals>();
        builder.Services.AddHostedService<SandboxReconciliation>();
        builder.Services.AddDbContext<PayDb>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Payment"))
        );
        builder.Services.AddScoped<PaymentRepository>();
        builder.Services.AddScoped<IPaymentReader>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IPaymentWriter>(s => s.GetRequiredService<PaymentRepository>());
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork<PayDb>>();
        builder.Services.AddScoped<IPaymentMethod, CashMethod>();
        builder.Services.AddScoped<IPaymentMethod, WalletMethod>();
        builder.Services.AddScoped<CashCollection>();
        builder.Services.AddScoped<PaymentMethodFactory>();
        builder.Services.AddScoped<PaymentCommands>();
        builder.Services.AddScoped<WalletTripWorkflow>();
        builder.Services.AddScoped<PaymentQueries>();
        builder.Services.AddScoped<IReadinessProbe, EfReadiness<PayDb>>();
        builder.AddReliableBus<PayDb>(x =>
        {
            x.AddConsumer<RideCompletedConsumer>();
            x.AddConsumer<WalletTripWorkflow>();
        });
    }
}
