using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;
using SwiftRide.Application.Persistence;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Infrastructure.Wallets;

namespace SwiftRide.Payments.Infrastructure;

public static class ServiceRegistration
{
    public static void AddPaymentInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(
            new PaymentSimulationSettings(
                builder.Configuration["Payments:SimulationOutcome"] ?? "Pending"
            )
        );
        builder.Services.AddScoped<IWalletFunding, WalletFunding>();
        builder.Services.AddScoped<IWalletAccounts, WalletAccounts>();
        builder.Services.AddScoped<IWalletWithdrawalStore, WalletWithdrawalStore>();
        builder.Services.AddScoped<WalletQueries>();
        builder.Services.AddScoped<WalletWithdrawals>();
        builder.Services.AddHostedService<WalletWithdrawalSimulationWorker>();
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
