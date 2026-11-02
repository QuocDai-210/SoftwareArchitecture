using SwiftRide.Application.Health;

namespace SwiftRide.Gateway;

public sealed class GatewayReadiness(IHttpClientFactory clients, ServiceUrls urls) : IReadinessProbe
{
    public async Task<bool> Ready(CancellationToken ct)
    {
        try
        {
            var results = await Task.WhenAll(
                new[] { urls.Trip, urls.Matching, urls.Payment }.Select(async url =>
                {
                    using var r = await clients
                        .CreateClient("screens")
                        .GetAsync($"{url}/health/ready", ct);
                    return r.IsSuccessStatusCode;
                })
            );
            return results.All(x => x);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
