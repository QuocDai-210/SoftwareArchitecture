using System.Net.Http.Json;

namespace SwiftRide.Trips.Infrastructure;

public sealed class MatchingHttpClient(IHttpClientFactory clients) : IMatchingClient
{
    public async Task EnsureArrival(ArrivalRequest request, string correlation, CancellationToken ct)
    {
        var client = clients.CreateClient("matching");
        using var message = new HttpRequestMessage(HttpMethod.Post, "/arrivals") { Content = JsonContent.Create(request) };
        message.Headers.Add("CorrelationId", correlation);
        using var response = await client.SendAsync(message, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) throw new RuleException("Xe chưa đến điểm đón/trả khách.");
        response.EnsureSuccessStatusCode();
    }
    public Task<MatchResponse> Match(
        MatchRequest request,
        string correlation,
        CancellationToken ct
    ) => Send<MatchResponse>("/match", request, correlation, ct);

    public Task<Quote> Quote(QuoteRequest request, string correlation, CancellationToken ct) =>
        Send<Quote>("/quote", request, correlation, ct);

    public Task<ReservationResult> Reserve(
        ReservationRequest request,
        string correlation,
        CancellationToken ct
    ) => Send<ReservationResult>("/reservations", request, correlation, ct);

    public Task<ReservationResult> Confirm(
        ReservationRequest request,
        string correlation,
        CancellationToken ct
    ) => Send<ReservationResult>("/reservations/confirm", request, correlation, ct);

    private async Task<T> Send<T>(
        string path,
        object body,
        string correlation,
        CancellationToken ct
    )
    {
        var client = clients.CreateClient("matching");
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(
                body,
                options: SwiftRide.Application.Serialization.ApplicationJson.Options
            ),
        };
        message.Headers.Add("CorrelationId", correlation);
        using var response = await client.SendAsync(message, ct);
        return await response.Read<T>(ct);
    }
}
