using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using MassTransit;
using MatchingService;
using MongoDB.Bson;
using MongoDB.Driver;
using SwiftRide.Core;
using SwiftRide.Hosting;

if (await Platform.HealthProbe(args)) return;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("matching");
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(builder.Configuration.GetConnectionString("Matching")));
builder.Services.AddSingleton(s => s.GetRequiredService<IMongoClient>().GetDatabase("MatchDB"));
builder.Services.AddSingleton<DriverRepository>();
builder.Services.AddSingleton<IPricingStrategy>(_ => new PromoPricing(new SurgePricing(new StandardPricing())));
builder.Services.AddSingleton<FareCalculator>();
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter(); x.AddConsumer<DriverEvents>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["Rabbit:Host"] ?? "rabbitmq", "/", host =>
        {
            host.Username(builder.Configuration["Rabbit:Username"]!); host.Password(builder.Configuration["Rabbit:Password"]!);
        });
        cfg.UseMessageRetry(r =>
        {
            r.Handle<Exception>(ex => Platform.IsTransient(ex) || ex is MongoException mongo &&
                (mongo.HasErrorLabel("RetryableWriteError") || mongo.HasErrorLabel("TransientTransactionError")));
            r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2));
        });
        cfg.ConfigureJsonSerializerOptions(options => { options.Converters.Add(new JsonStringEnumConverter()); return options; });
        cfg.ConfigureEndpoints(context);
    });
});
var app = builder.Build(); app.UsePlatform();
await app.Services.GetRequiredService<DriverRepository>().Initialize(CancellationToken.None);
app.MapGet("/health/ready", async (IMongoDatabase db, CancellationToken ct) =>
{
    await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct); return Results.Ok(new { status = "ready" });
}).AllowAnonymous();
app.MapPost("/match", async (MatchRequest request, DriverRepository drivers, CancellationToken ct) =>
{
    var watch = Stopwatch.StartNew();
    try { return Results.Json(new MatchResponse(await drivers.Match(request, ct)), Platform.Json); }
    finally { Telemetry.Matching.Record(watch.Elapsed.TotalSeconds); }
}).RequireAuthorization("service");
app.MapPost("/quote", (QuoteRequest request, FareCalculator calculator, IConfiguration config) =>
{
    var route = Routes.Estimate(request.Pickup, request.Destination);
    var fare = new FareInput(route.DistanceKm, route.DurationMinutes,
        Surge: config.GetValue("Pricing:Surge", 1m), Toll: config.GetValue("Pricing:Toll", 0m),
        Promotion: config.GetValue("Pricing:Promotion", 1m), Tax: config.GetValue("Pricing:Tax", 0.1m));
    return new Quote(Guid.NewGuid(), calculator.Calculate(fare), "VND", fare, DateTimeOffset.UtcNow.AddSeconds(120));
}).RequireAuthorization();
app.MapPost("/reservations", (ReservationRequest request, DriverRepository drivers, CancellationToken ct) => drivers.Reserve(request, ct)).RequireAuthorization("service");
app.MapPost("/reservations/confirm", (ReservationRequest request, DriverRepository drivers, CancellationToken ct) => drivers.Confirm(request, ct)).RequireAuthorization("service");
app.MapPost("/reservations/release", async (ReservationRequest request, DriverRepository drivers, CancellationToken ct) =>
{
    await drivers.Release(request.TripId, request.ReservationId, ct); return Results.NoContent();
}).RequireAuthorization("service");
app.MapGet("/drivers/me", async (ClaimsPrincipal user, DriverRepository drivers, CancellationToken ct) =>
{
    var driver = await drivers.Drivers.Find(x => x.Id == user.Subject()).FirstOrDefaultAsync(ct);
    return driver is null ? Results.NotFound() : Results.Ok(new { driver.Id, driver.Name, driver.Available, driver.Busy,
        driver.LastSeenAt, latitude = driver.Location.Coordinates.Latitude, longitude = driver.Location.Coordinates.Longitude });
}).RequireAuthorization("driver");
app.MapPut("/drivers/me/location", async (LocationRequest request, ClaimsPrincipal user, DriverRepository drivers, CancellationToken ct) =>
{
    new Point(request.Latitude, request.Longitude, "Driver").Validate();
    await drivers.Drivers.UpdateOneAsync(x => x.Id == user.Subject(), Builders<Driver>.Update
        .Set(x => x.Location, Driver.Geo(request.Longitude, request.Latitude)).Set(x => x.LastSeenAt, DateTime.UtcNow)
        .SetOnInsert(x => x.Name, user.Identity?.Name ?? "Driver").SetOnInsert(x => x.Busy, false).SetOnInsert(x => x.Available, false), new UpdateOptions { IsUpsert = true }, ct);
    return Results.NoContent();
}).RequireAuthorization("driver");
app.MapPut("/drivers/me/availability", async (AvailabilityRequest request, ClaimsPrincipal user, DriverRepository drivers, CancellationToken ct) =>
{
    var result = await drivers.Drivers.UpdateOneAsync(x => x.Id == user.Subject(), Builders<Driver>.Update.Set(x => x.Available, request.Available), cancellationToken: ct);
    return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("driver");
app.Run();
