using System.Net;
using System.Text.Json;
using SwiftRide.Infrastructure;

namespace SwiftRide.Gateway;

public static class TripScreen
{
    public static async Task<IResult> Get(
        Guid id,
        HttpContext http,
        ServiceUrls urls,
        IHttpClientFactory clients,
        CancellationToken ct
    )
    {
        var client = clients.CreateClient("screen");
        client.DefaultRequestHeaders.Authorization =
            System.Net.Http.Headers.AuthenticationHeaderValue.Parse(
                http.Request.Headers.Authorization.ToString()
            );
        client.DefaultRequestHeaders.Add("CorrelationId", http.Correlation());
        http.Response.Headers.CacheControl = "private, no-store";
        using var response = await client.GetAsync($"{urls.Trip}/trips/{id}", ct);
        if (!response.IsSuccessStatusCode)
            return Results.StatusCode((int)response.StatusCode);
        var trip = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        JsonElement? payment = null;
        var paymentUnavailable = false;
        if (
            http.User.IsInRole("rider")
            && trip.GetProperty("riderId").GetString() == http.User.Subject()
            && trip.GetProperty("status").GetString()
                is "PaymentPending"
                    or "PaymentReview"
                    or "Paid"
        )
        {
            try
            {
                using var result = await client.GetAsync($"{urls.Payment}/payments/{id}", ct);
                if (result.IsSuccessStatusCode)
                    payment = await result.Content.ReadFromJsonAsync<JsonElement>(ct);
                else if (result.StatusCode is not HttpStatusCode.NotFound)
                    paymentUnavailable = true;
            }
            catch (Exception ex)
                when (!ct.IsCancellationRequested
                    && ex is HttpRequestException or OperationCanceledException
                )
            {
                paymentUnavailable = true;
            }
        }
        return Results.Ok(
            new
            {
                trip,
                payment,
                paymentUnavailable,
            }
        );
    }
}
