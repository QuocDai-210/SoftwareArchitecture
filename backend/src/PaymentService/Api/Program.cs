using SwiftRide.Payments.Api;
using SwiftRide.Payments.Infrastructure;

if (await CommandLineHealthProbe.Run(args))
    return;
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("payment");
builder.AddPaymentInfrastructure();
var app = builder.Build();
app.UsePlatform();
await app.Services.InitializePaymentDatabase();
app.MapPaymentEndpoints();
app.MapSandboxEndpoints();
app.MapWalletEndpoints();
app.Run();
