using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace SwiftRide.Infrastructure;

public sealed class ServiceTokens(IHttpClientFactory clients, IConfiguration config)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string> Get(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && _expires > DateTimeOffset.UtcNow.AddSeconds(30))
                return _token;
            using var response = await clients
                .CreateClient("identity")
                .PostAsync(
                    $"{config["Auth:Authority"]}/protocol/openid-connect/token",
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["grant_type"] = "client_credentials",
                            ["client_id"] = "swiftride-service",
                            ["client_secret"] = config["Auth:ServiceSecret"]!,
                        }
                    ),
                    ct
                );
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _token = data.GetProperty("access_token").GetString()!;
            _expires = DateTimeOffset.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32());
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
