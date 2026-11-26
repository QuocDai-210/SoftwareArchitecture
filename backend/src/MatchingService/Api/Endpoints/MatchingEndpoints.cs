namespace SwiftRide.Matching.Api;

public static class MatchingEndpoints
{
    public static void MapMatchingEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/match",
                (MatchRequest request, FindDriver useCase, CancellationToken ct) =>
                    useCase.Execute(request, ct)
            )
            .RequireAuthorization("service");
        app.MapPost("/quote", (QuoteRequest request, QuoteTrip useCase) => useCase.Execute(request))
            .RequireAuthorization("service");
        app.MapPost(
                "/reservations",
                (ReservationRequest request, DriverReservations useCase, CancellationToken ct) =>
                    useCase.Reserve(request, ct)
            )
            .RequireAuthorization("service");
        app.MapPost(
                "/reservations/confirm",
                (ReservationRequest request, DriverReservations useCase, CancellationToken ct) =>
                    useCase.Confirm(request, ct)
            )
            .RequireAuthorization("service");
        app.MapGet(
                "/drivers/me",
                (ClaimsPrincipal user, DriverQueries queries, CancellationToken ct) =>
                    queries.Me(user.Actor(), ct)
            )
            .RequireAuthorization("driver");
        app.MapPut(
                "/drivers/me/location",
                async (
                    LocationRequest request,
                    ClaimsPrincipal user,
                    DriverPresence commands,
                    CancellationToken ct
                ) =>
                {
                    await commands.Location(
                        user.Actor(),
                        user.Identity?.Name ?? "Driver",
                        request,
                        ct
                    );
                    return Results.NoContent();
                }
            )
            .RequireAuthorization("driver");
        app.MapPut(
                "/drivers/me/availability",
                async (
                    AvailabilityRequest request,
                    ClaimsPrincipal user,
                    DriverPresence commands,
                    CancellationToken ct
                ) =>
                {
                    await commands.Availability(user.Actor(), request, ct);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization("driver");
    }
}
