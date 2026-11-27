namespace SwiftRide.Trips.Application.Ports;

public interface IMatchingClient
{
    Task<MatchResponse> Match(MatchRequest request, string correlation, CancellationToken ct);
    Task<Quote> Quote(QuoteRequest request, string correlation, CancellationToken ct);
    Task<ReservationResult> Reserve(
        ReservationRequest request,
        string correlation,
        CancellationToken ct
    );
    Task<ReservationResult> Confirm(
        ReservationRequest request,
        string correlation,
        CancellationToken ct
    );
}
