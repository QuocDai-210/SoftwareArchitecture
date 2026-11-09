using SwiftRide.Trips.Domain;

namespace SwiftRide.Trips.Api;

public static class TripEndpoints
{
    public static void MapTripEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/profiles/me",
                async (ClaimsPrincipal user, ProfileCommands commands, CancellationToken ct) =>
                    Results.Ok(await commands.Get(user.Actor(), ct))
            )
            .RequireAuthorization();
        app.MapPut(
                "/profiles/me",
                (
                    ProfileRequest request,
                    ClaimsPrincipal user,
                    ProfileCommands commands,
                    CancellationToken ct
                ) => commands.Update(request, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/trips/history",
                (
                    int? page,
                    string? status,
                    ClaimsPrincipal user,
                    TripQueries queries,
                    CancellationToken ct
                ) => queries.History(user.Actor(), page ?? 1, status, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/trips/statistics",
                (ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.Statistics(user.Actor(), ct)
            )
            .RequireAuthorization("ops");
        app.MapGet(
                "/ops/trips/{id:guid}/workflow",
                (Guid id, ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.Workflow(id, user.Actor(), ct)
            )
            .RequireAuthorization("ops");
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
            .RequireAuthorization();
        app.MapGet(
                "/trips/{id:guid}",
                (Guid id, ClaimsPrincipal user, TripQueries queries, CancellationToken ct) =>
                    queries.Get(id, user.Actor(), ct)
            )
            .RequireAuthorization();
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
            .RequireAuthorization();
        app.MapPost(
                "/trips/{id:guid}/review",
                (
                    Guid id,
                    ReviewTripRequest request,
                    ClaimsPrincipal user,
                    TripCommands commands,
                    CancellationToken ct
                ) => commands.Review(id, request, user.Actor(), ct)
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
            .RequireAuthorization("ops");
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
