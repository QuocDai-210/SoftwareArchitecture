using System.Net.Http.Headers;

namespace SwiftRide.Infrastructure;

public sealed class ServiceAuthHandler(ServiceTokens tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await tokens.Get(ct)
        );
        return await base.SendAsync(request, ct);
    }
}
