using SwiftRide.Gateway;
using SwiftRide.Infrastructure;

if (await CommandLineHealthProbe.Run(args))
    return;
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("gateway");
builder.AddGateway();
var app = builder.Build();
app.UsePlatform();
app.UseRateLimiter();
app.MapGet("/screens/trips/{id:guid}", TripScreen.Get).RequireAuthorization("participant");
app.MapReverseProxy();
app.Run();
