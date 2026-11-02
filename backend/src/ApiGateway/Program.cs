using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SwiftRide.Hosting;
using Yarp.ReverseProxy.Configuration;

if (await Platform.HealthProbe(args)) return;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("gateway");
var tripUrl = builder.Configuration["Services:Trip"] ?? "http://trip:8080";
var matchUrl = builder.Configuration["Services:Matching"] ?? "http://matching:8080";
var payUrl = builder.Configuration["Services:Payment"] ?? "http://payment:8080";
builder.Services.AddHttpClient("screens", c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var booking = context.Request.Method == "POST" && context.Request.Path == "/trips";
        var identity = context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"{identity}:{booking}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = booking ? 10 : 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});
var routes = new List<RouteConfig>
{
    new() { RouteId = "offers", ClusterId = "trip", Order = 0, Match = new() { Path = "/drivers/me/offers" } },
    new() { RouteId = "trips", ClusterId = "trip", Match = new() { Path = "/trips/{**rest}" } },
    new() { RouteId = "drivers", ClusterId = "matching", Order = 10, Match = new() { Path = "/drivers/{**rest}" } },
    new() { RouteId = "quote", ClusterId = "matching", Match = new() { Path = "/quote" } },
    new() { RouteId = "payments", ClusterId = "payment", Match = new() { Path = "/payments/{**rest}" } },
    new() { RouteId = "wallets", ClusterId = "payment", Match = new() { Path = "/wallets/{**rest}" } }
};
foreach (var name in new[] { "trip", "matching", "payment" })
    routes.Add(new RouteConfig { RouteId = $"docs-{name}", ClusterId = name, AuthorizationPolicy = "ops",
        Match = new() { Path = $"/openapi/{name}.json" },
        Transforms = [new Dictionary<string, string> { ["PathSet"] = "/openapi/v1.json" }] });
ClusterConfig Cluster(string id, string url) => new()
{
    ClusterId = id, Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = url } },
    HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(20) }
};
builder.Services.AddReverseProxy().LoadFromMemory(routes,
    [Cluster("trip", tripUrl), Cluster("matching", matchUrl), Cluster("payment", payUrl)]);
var app = builder.Build(); app.UsePlatform(); app.UseRateLimiter();
app.MapGet("/health/ready", async (IHttpClientFactory clients, CancellationToken ct) =>
{
    try
    {
        var results = await Task.WhenAll(new[] { tripUrl, matchUrl, payUrl }.Select(async url =>
        { using var r = await clients.CreateClient("screens").GetAsync($"{url}/health/ready", ct); return r.IsSuccessStatusCode; }));
        return results.All(x => x) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
    }
    catch (HttpRequestException) { return Results.StatusCode(503); }
    catch (TaskCanceledException) { return Results.StatusCode(503); }
}).AllowAnonymous();
app.MapGet("/screens/trips/{id:guid}", async (Guid id, HttpContext http, IHttpClientFactory clients, CancellationToken ct) =>
{
    var client = clients.CreateClient("screens");
    client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(http.Request.Headers.Authorization.ToString());
    client.DefaultRequestHeaders.Add("CorrelationId", http.Correlation());
    using var tripResponse = await client.GetAsync($"{tripUrl}/trips/{id}", ct);
    if (!tripResponse.IsSuccessStatusCode) return Results.StatusCode((int)tripResponse.StatusCode);
    var trip = await tripResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
    JsonElement? payment = null;
    var deferred = false;
    if (trip.GetProperty("status").GetString() is "Paid" or "PaymentPending" or "PaymentReview")
    {
        try
        {
            using var payResponse = await client.GetAsync($"{payUrl}/payments/{id}", ct);
            if (payResponse.IsSuccessStatusCode) payment = await payResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            else deferred = (int)payResponse.StatusCode >= 500;
        }
        catch (HttpRequestException) { deferred = true; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { deferred = true; }
    }
    return Results.Ok(new { trip, payment, paymentUnavailable = deferred });
}).RequireAuthorization();
app.MapReverseProxy().RequireAuthorization();
app.Run();
