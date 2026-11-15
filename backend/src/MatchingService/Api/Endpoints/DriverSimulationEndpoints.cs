using SwiftRide.Matching.Domain;

namespace SwiftRide.Matching.Api;

public static class DriverSimulationEndpoints
{
    public static void MapDriverSimulationEndpoints(this WebApplication app)
    {
        app.MapPut(
                "/drivers/me/heartbeat",
                async (ClaimsPrincipal user, DriverPresence commands, CancellationToken ct) =>
                {
                    await commands.Heartbeat(user.Actor(), ct);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization("driver");
        app.MapGet(
                "/simulations/{driverId}",
                async (
                    string driverId,
                    ClaimsPrincipal user,
                    SimulationWorkflow simulation,
                    CancellationToken ct
                ) =>
                {
                    var value = await simulation.Get(driverId, user.Actor(), ct);
                    return value is null
                        ? Results.Content("null", "application/json")
                        : Results.Ok(value);
                }
            )
            .RequireAuthorization();
        app.MapPut(
                "/simulations/{driverId}",
                (
                    string driverId,
                    SimulationControlRequest request,
                    ClaimsPrincipal user,
                    SimulationWorkflow simulation,
                    CancellationToken ct
                ) => simulation.Control(driverId, request.Speed, request.Paused, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapPost(
                "/arrivals",
                async (
                    ArrivalRequest request,
                    SimulationWorkflow simulation,
                    CancellationToken ct
                ) =>
                {
                    await simulation.EnsureArrival(
                        request.DriverId,
                        request.TripId,
                        request.Target,
                        ct
                    );
                    return Results.NoContent();
                }
            )
            .RequireAuthorization("service");
    }
}
