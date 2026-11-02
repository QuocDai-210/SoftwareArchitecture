using SwiftRide.Matching.Api;
using SwiftRide.Matching.Infrastructure;

if (await CommandLineHealthProbe.Run(args))
    return;
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("matching");
builder.AddMatchingInfrastructure();
var app = builder.Build();
app.UsePlatform();
await app.Services.InitializeMatchingDatabase();
app.MapMatchingEndpoints();
app.Run();
