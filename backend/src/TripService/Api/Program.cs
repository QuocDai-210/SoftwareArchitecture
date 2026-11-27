using SwiftRide.Trips.Api;
using SwiftRide.Trips.Infrastructure;

if (await CommandLineHealthProbe.Run(args))
    return;
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("trip");
builder.AddTripInfrastructure();
var app = builder.Build();
app.UsePlatform();
await app.Services.InitializeTripDatabase();
app.MapTripEndpoints();
app.Run();
