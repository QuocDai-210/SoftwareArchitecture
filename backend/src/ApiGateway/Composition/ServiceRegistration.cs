using System.Threading.RateLimiting;
using SwiftRide.Application.Health;
using Yarp.ReverseProxy.Configuration;

namespace SwiftRide.Gateway;

public static class ServiceRegistration
{
    public static void AddGateway(this WebApplicationBuilder builder)
    {
        var urls = new ServiceUrls(
            builder.Configuration["Services:Trip"] ?? "http://trip:8080",
            builder.Configuration["Services:Matching"] ?? "http://matching:8080",
            builder.Configuration["Services:Payment"] ?? "http://payment:8080"
        );
        builder.Services.AddSingleton(urls);
        builder.Services.AddScoped<IReadinessProbe, GatewayReadiness>();
        var tripUrl = urls.Trip;
        var matchUrl = urls.Matching;
        var payUrl = urls.Payment;
        builder.Services.AddHttpClient("readiness", c => c.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var booking =
                    context.Request.Method == "POST"
                    && string.Equals(
                        context.Request.Path.Value?.TrimEnd('/'),
                        "/trips",
                        StringComparison.OrdinalIgnoreCase
                    );
                var reading = HttpMethods.IsGet(context.Request.Method);
                var category =
                    booking ? "booking"
                    : reading ? "read"
                    : "write";
                var identity =
                    context.User.FindFirst("sub")?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{identity}:{category}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        // Allow two tracking tabs without consuming the command budget.
                        PermitLimit =
                            booking ? 10
                            : reading ? 360
                            : 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }
                );
            });
        });
        var routes = new List<RouteConfig>();
        void Route(string path, string cluster, string policy, params string[] methods) =>
            routes.Add(
                new RouteConfig
                {
                    RouteId = $"{cluster}-{path}-{string.Join('-', methods)}",
                    ClusterId = cluster,
                    AuthorizationPolicy = policy,
                    Match = new() { Path = path, Methods = methods },
                }
            );
        Route("/trips", "trip", "rider", "POST");
        Route("/trips", "trip", "participant", "GET");
        Route("/trips/{id:guid}", "trip", "participant", "GET");
        foreach (var action in new[] { "confirm-quote", "cancel" })
            Route($"/trips/{{id:guid}}/{action}", "trip", "rider", "POST");
        foreach (
            var action in new[]
            {
                "driver-accept",
                "driver-reject",
                "en-route",
                "pickup",
                "dropoff",
            }
        )
            Route($"/trips/{{id:guid}}/{action}", "trip", "driver", "POST");
        Route("/trips/{id:guid}/retry-payment", "trip", "service", "POST");
        Route("/drivers/me/offers", "trip", "driver", "GET");
        Route("/drivers/me", "matching", "driver", "GET");
        foreach (var action in new[] { "location", "availability" })
            Route($"/drivers/me/{action}", "matching", "driver", "PUT");
        foreach (var path in new[] { "/match", "/quote", "/reservations", "/reservations/confirm" })
            Route(path, "matching", "service", "POST");
        Route("/payments", "payment", "service", "POST");
        Route("/payments/{id:guid}/refund", "payment", "service", "POST");
        Route("/payments/{id:guid}", "payment", "payment-read", "GET");
        Route("/payments/{id:guid}/ledger", "payment", "payment-read", "GET");
        Route("/wallets/me", "payment", "rider", "GET");
        foreach (var name in new[] { "trip", "matching", "payment" })
            routes.Add(
                new RouteConfig
                {
                    RouteId = $"docs-{name}",
                    ClusterId = name,
                    AuthorizationPolicy = "default",
                    Match = new() { Path = $"/openapi/{name}.json" },
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathSet"] = "/openapi/v1.json" },
                    ],
                }
            );
        ClusterConfig Cluster(string id, string url) =>
            new()
            {
                ClusterId = id,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["primary"] = new() { Address = url },
                },
                HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(20) },
            };
        builder
            .Services.AddReverseProxy()
            .LoadFromMemory(
                routes,
                [
                    Cluster("trip", tripUrl),
                    Cluster("matching", matchUrl),
                    Cluster("payment", payUrl),
                ]
            );
    }
}
