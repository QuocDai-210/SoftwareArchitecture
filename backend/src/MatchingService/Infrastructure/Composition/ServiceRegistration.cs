using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using SwiftRide.Application.Health;

namespace SwiftRide.Matching.Infrastructure;

public static class ServiceRegistration
{
    public static void AddMatchingInfrastructure(this WebApplicationBuilder builder)
    {
        MongoDB.Bson.Serialization.BsonSerializer.TryRegisterSerializer(
            new MongoDB.Bson.Serialization.Serializers.GuidSerializer(
                MongoDB.Bson.GuidRepresentation.Standard
            )
        );
        builder.Services.AddMemoryCache(options => options.SizeLimit = 128);
        builder
            .Services.AddHttpClient("geoapify", client =>
            {
                client.BaseAddress = new Uri("https://api.geoapify.com/v1/");
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .RemoveAllLoggers()
            .AddHttpMessageHandler<InternalRetryHandler>();
        if (builder.Configuration["Maps:Provider"] == "Test")
            builder.Services.AddSingleton<IMapProvider, TestMapProvider>();
        else
            builder.Services.AddSingleton<IMapProvider, GeoapifyMapProvider>();
        builder.Services.AddScoped<MapQueries>();
        builder.Services.AddScoped<ISimulationStore, MongoSimulationStore>();
        builder.Services.AddScoped<SimulationWorkflow>();
        builder.Services.AddSingleton(
            new SimulationSettings(
                builder.Configuration.GetValue("Simulation:EnforceArrival", true)
            )
        );
        builder.Services.AddHostedService<SimulationWorker>();
        builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(
            builder.Configuration.GetConnectionString("Matching")
        ));
        builder.Services.AddSingleton(s =>
            s.GetRequiredService<IMongoClient>().GetDatabase("MatchDB")
        );
        builder.Services.AddHostedService<SeededDriverHeartbeatWorker>();
        builder.Services.AddSingleton<MongoDriverRepository>();
        builder.Services.AddSingleton<IDriverReader>(s =>
            s.GetRequiredService<MongoDriverRepository>()
        );
        builder.Services.AddSingleton<IDriverPresence>(s =>
            s.GetRequiredService<MongoDriverRepository>()
        );
        builder.Services.AddSingleton<IDriverEligibility>(s =>
            s.GetRequiredService<MongoDriverRepository>()
        );
        builder.Services.AddSingleton<IDriverReservations>(s =>
            s.GetRequiredService<MongoDriverRepository>()
        );
        builder.Services.AddSingleton<DriverDatabaseInitializer>();
        builder.Services.AddSingleton<IPricingStrategy>(_ => new PromoPricing(
            new SurgePricing(new StandardPricing())
        ));
        builder.Services.AddSingleton<FareCalculator>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(
            new PricingSettings(
                builder.Configuration.GetValue("Pricing:Surge", 1m),
                builder.Configuration.GetValue("Pricing:Toll", 0m),
                builder.Configuration.GetValue("Pricing:Promotion", 1m),
                builder.Configuration.GetValue("Pricing:Tax", 0.1m)
            )
        );
        builder.Services.AddScoped<QuoteTrip>();
        builder.Services.AddScoped<FindDriver>();
        builder.Services.AddScoped<DriverQueries>();
        builder.Services.AddScoped<DriverPresence>();
        builder.Services.AddScoped<DriverReservations>();
        builder.Services.AddScoped<IReadinessProbe, MongoReadiness>();
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<DriverEvents>();
            x.UsingRabbitMq(
                (context, cfg) =>
                {
                    cfg.Host(builder.Configuration["Rabbit:Host"] ?? "rabbitmq", "/", host =>
                    {
                        host.Username(builder.Configuration["Rabbit:Username"]!);
                        host.Password(builder.Configuration["Rabbit:Password"]!);
                    });
                    cfg.UseMessageRetry(r =>
                    {
                        r.Handle<Exception>(ex =>
                            TransientErrors.IsTransient(ex)
                            || ex is SimulationConflictException
                            || ex is MongoException mongo
                                && (
                                    mongo.HasErrorLabel("RetryableWriteError")
                                    || mongo.HasErrorLabel("TransientTransactionError")
                                )
                        );
                        r.Exponential(
                            3,
                            TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(15),
                            TimeSpan.FromSeconds(2)
                        );
                    });
                    cfg.ConfigureJsonSerializerOptions(options =>
                    {
                        options.Converters.Add(new JsonStringEnumConverter());
                        return options;
                    });
                    cfg.ConfigureEndpoints(context);
                }
            );
        });
    }

    public static Task InitializeMatchingDatabase(this IServiceProvider services) =>
        services.GetRequiredService<DriverDatabaseInitializer>().Initialize(CancellationToken.None);
}
