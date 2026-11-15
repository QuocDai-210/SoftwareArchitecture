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
        builder.Services.AddHttpClient("screens", c => c.Timeout = TimeSpan.FromSeconds(5));
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
                var identity = context.User.FindFirst("sub")?.Value
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
        var routes = new List<RouteConfig>
        {
            new()
            {
                RouteId = "auth-login",
                ClusterId = "trip",
                Order = -1,
                AuthorizationPolicy = "anonymous",
                Match = new() { Path = "/auth/login", Methods = ["POST"] },
            },
            new()
            {
                RouteId = "offers",
                ClusterId = "trip",
                Order = 0,
                Match = new() { Path = "/drivers/me/offers" },
            },
            new()
            {
                RouteId = "trips",
                ClusterId = "trip",
                Match = new() { Path = "/trips/{**rest}" },
            },
            new()
            {
                RouteId = "drivers",
                ClusterId = "matching",
                Order = 10,
                Match = new() { Path = "/drivers/{**rest}" },
            },
            new()
            {
                RouteId = "quote",
                ClusterId = "matching",
                Match = new() { Path = "/quote" },
            },
            new()
            {
                RouteId = "payments",
                ClusterId = "payment",
                Match = new() { Path = "/payments/{**rest}" },
            },
            new()
            {
                RouteId = "wallets",
                ClusterId = "payment",
                Match = new() { Path = "/wallets/{**rest}" },
            },
        };
        foreach (var prefix in new[] { "match", "reservations", "arrivals" })
        {
            routes.Add(
                new RouteConfig
                {
                    RouteId = $"internal-{prefix}",
                    ClusterId = "matching",
                    AuthorizationPolicy = "service",
                    Match = new() { Path = "/" + prefix + "/{**rest}" },
                }
            );
        }
        foreach (
            var (prefix, cluster) in new[]
            {
                ("profiles", "trip"),
                ("maps", "matching"),
                ("simulations", "matching"),
            }
        )
            routes.Add(
                new RouteConfig
                {
                    RouteId = prefix,
                    ClusterId = cluster,
                    Match = new() { Path = "/" + prefix + "/{**rest}" },
                }
            );
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
