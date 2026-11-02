using System.Net.Http.Headers;
using System.Text.Json;
using SwiftRide.Infrastructure;

namespace SwiftRide.Gateway;

public static class ScreenEndpoints
{
    public static void MapGatewayEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/screens/trips/{id:guid}",
                async (
                    Guid id,
                    HttpContext http,
                    IHttpClientFactory clients,
                    ServiceUrls urls,
                    CancellationToken ct
                ) =>
                {
                    var client = clients.CreateClient("screens");
                    client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                        http.Request.Headers.Authorization.ToString()
                    );
                    client.DefaultRequestHeaders.Add("CorrelationId", http.Correlation());
                    using var tripResponse = await client.GetAsync($"{urls.Trip}/trips/{id}", ct);
                    if (!tripResponse.IsSuccessStatusCode)
                        return Results.StatusCode((int)tripResponse.StatusCode);
                    var trip = await tripResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                    JsonElement? payment = null;
                    var deferred = false;
                    if (
                        trip.GetProperty("status").GetString()
                        is "Paid"
                            or "PaymentPending"
                            or "PaymentReview"
                    )
                    {
                        try
                        {
                            using var payResponse = await client.GetAsync(
                                $"{urls.Payment}/payments/{id}",
                                ct
                            );
                            if (payResponse.IsSuccessStatusCode)
                                payment = await payResponse.Content.ReadFromJsonAsync<JsonElement>(
                                    ct
                                );
                            else
                                deferred = (int)payResponse.StatusCode >= 500;
                        }
                        catch (HttpRequestException)
                        {
                            deferred = true;
                        }
                        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                        {
                            deferred = true;
                        }
                    }
                    JsonElement? simulation = null;
                    if (trip.TryGetProperty("driverId", out var driverId) && driverId.ValueKind == JsonValueKind.String)
                    {
                        try
                        {
                            var matching = clients.CreateClient("matching");
                            using var simulationResponse = await matching.GetAsync($"/simulations/{Uri.EscapeDataString(driverId.GetString()!)}", ct);
                            if (simulationResponse.IsSuccessStatusCode)
                            {
                                var value = await simulationResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                                if (value.ValueKind == JsonValueKind.Object && value.GetProperty("tripId").GetGuid() == id) simulation = value;
                            }
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
                    }
                    return Results.Ok(new { trip, payment, simulation, paymentUnavailable = deferred });
                }
            )
            .RequireAuthorization();
        app.MapReverseProxy().RequireAuthorization();
    }
}
