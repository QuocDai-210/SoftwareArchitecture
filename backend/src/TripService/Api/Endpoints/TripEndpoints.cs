using SwiftRide.Trips.Domain;

namespace SwiftRide.Trips.Api;

public static class TripEndpoints
{
    public static void MapTripEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/trips",
                async (
                    CreateTripRequest request,
                    HttpContext http,
                    TripCommands commands,
                    CancellationToken ct
                ) =>
                {
                    var result = await commands.Create(
                        request,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault()!,
                        http.User.Actor(),
                        http.Correlation(),
                        ct
                    );
                    return result.IsNew
                        ? Results.Created($"/trips/{result.Id}", result.View)
                        : Results.Ok(result.View);
                }
            )
            .RequireAuthorization("rider");
        app.MapGet(
                "/trips",
                (ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.List(user.Actor(), ct)
            )
            .RequireAuthorization("participant");
        app.MapGet(
                "/trips/{id:guid}",
                (Guid id, ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.Get(id, user.Actor(), ct)
            )
            .RequireAuthorization("participant");
        app.MapGet(
                "/drivers/me/offers",
                (ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.Offers(user.Actor(), ct)
            )
            .RequireAuthorization("driver");
        app.MapPost(
                "/trips/{id:guid}/confirm-quote",
                (
                    Guid id,
                    ConfirmQuoteRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.ConfirmQuote(id, request, user.Actor(), ct)
            )
            .RequireAuthorization("rider");
        app.MapPost(
                "/trips/{id:guid}/cancel",
                (
                    Guid id,
                    CancelTripRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.Cancel(id, request, user.Actor(), ct)
            )
            .RequireAuthorization("rider");
        app.MapPost(
                "/trips/{id:guid}/driver-reject",
                (
                    Guid id,
                    VersionRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.RejectDriver(id, request, user.Actor(), ct)
            )
            .RequireAuthorization("driver");
        app.MapPost(
                "/trips/{id:guid}/driver-accept",
                (
                    Guid id,
                    VersionRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.AcceptDriver(id, request, user.Actor(), ct)
            )
            .RequireAuthorization("driver");
        app.MapPost(
                "/trips/{id:guid}/retry-payment",
                (
                    Guid id,
                    VersionRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.RetryPayment(id, request, user.Actor(), ct)
            )
            .RequireAuthorization("service");
        foreach (
            var (route, target) in new[]
            {
                ("en-route", TripStatus.EnRoute),
                ("pickup", TripStatus.PickedUp),
                ("dropoff", TripStatus.DroppedOff),
            }
        )
            app.MapPost(
                    $"/trips/{{id:guid}}/{route}",
                    (
                        Guid id,
                        VersionRequest request,
                        ClaimsPrincipal user,
                        TripCommands commands,
                        CancellationToken ct
                    ) => commands.Move(id, request, user.Actor(), target, ct)
                )
                .RequireAuthorization("driver");
    }
}
