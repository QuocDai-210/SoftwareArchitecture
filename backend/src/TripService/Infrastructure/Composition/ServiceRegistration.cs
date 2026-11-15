using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;

namespace SwiftRide.Trips.Infrastructure;

public static class ServiceRegistration
{
    public static void AddTripInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<TripDb>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Trip"))
        );
        builder.Services.AddScoped<TripRepository>();
        builder.Services.AddSingleton(
            new TripSimulationSettings(
                builder.Configuration.GetValue<bool>("Simulation:AutoAcceptDrivers")
            )
        );
        builder.Services.AddScoped<LocalAuthentication>();
        builder.Services.AddScoped<TripAccountSeeder>();
        builder.Services.AddScoped<IUserProfileStore, UserProfileStore>();
        builder.Services.AddScoped<ProfileCommands>();
        builder.Services.AddScoped<ITripReader>(s => s.GetRequiredService<TripRepository>());
        builder.Services.AddScoped<ITripWriter>(s => s.GetRequiredService<TripRepository>());
        builder.Services.AddScoped<ITripWorkflowQueue>(s => s.GetRequiredService<TripRepository>());
        builder.Services.AddScoped<IMatchingClient, MatchingHttpClient>();
        builder.Services.AddScoped<TripCommands>();
        builder.Services.AddScoped<TripQueries>();
        builder.Services.AddScoped<TripWorkflow>();
        builder.Services.AddScoped<TripPaymentNotifications>();
        builder.Services.AddScoped<IReadinessProbe, EfReadiness<TripDb>>();
        builder.AddReliableBus<TripDb>(x =>
        {
            x.AddConsumer<SettledConsumer>();
            x.AddConsumer<FailedConsumer>();
            x.AddConsumer<PaymentFaultConsumer>();
            x.AddConsumer<WalletHoldSucceededConsumer>();
            x.AddConsumer<WalletHoldFailedConsumer>();
        });
        builder.Services.AddScoped<WalletHoldSucceededConsumer>();
        builder.Services.AddScoped<WalletHoldFailedConsumer>();
        builder.Services.AddHostedService<TripWorkflowWorker>();
    }

    public static async Task InitializeTripDatabase(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TripDb>().Database.MigrateAsync();
        await scope
            .ServiceProvider.GetRequiredService<TripAccountSeeder>()
            .Seed(CancellationToken.None);
    }
}
