using SwiftRide.Matching.Domain;

namespace SwiftRide.Matching.Api;

public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        app.MapGet("/ops/drivers", (ClaimsPrincipal user, DriverAdministration commands, CancellationToken ct) => commands.List(user.Actor(), ct)).RequireAuthorization("ops");
        app.MapPost("/ops/drivers", (DriverApplication request, ClaimsPrincipal user, DriverAdministration commands, CancellationToken ct) => commands.Create(request, user.Actor(), ct)).RequireAuthorization("ops");
        app.MapPost("/ops/drivers/{id:guid}/approve", (Guid id, ApprovalRequest request, ClaimsPrincipal user, DriverAdministration commands, CancellationToken ct) => commands.Approve(id, request.TemporaryPassword, user.Actor(), ct)).RequireAuthorization("ops");
        app.MapPost("/ops/drivers/{id:guid}/reject", (Guid id, RejectionRequest request, ClaimsPrincipal user, DriverAdministration commands, CancellationToken ct) => commands.Reject(id, request.Reason, user.Actor(), ct)).RequireAuthorization("ops");
        app.MapPut("/ops/drivers/{subject}/suspension", async (string subject, SuspensionRequest request, ClaimsPrincipal user, DriverAdministration commands, CancellationToken ct) => { await commands.Suspend(subject, request.Suspended, user.Actor(), ct); return Results.NoContent(); }).RequireAuthorization("ops");
        app.MapPut("/drivers/me/heartbeat", async (ClaimsPrincipal user, DriverPresence commands, CancellationToken ct) => { await commands.Heartbeat(user.Actor(), ct); return Results.NoContent(); }).RequireAuthorization("driver");
        app.MapGet("/simulations/{driverId}", async (string driverId, ClaimsPrincipal user, SimulationWorkflow simulation, CancellationToken ct) => Results.Ok(await simulation.Get(driverId, user.Actor(), ct))).RequireAuthorization();
        app.MapPut("/simulations/{driverId}", (string driverId, SimulationControlRequest request, ClaimsPrincipal user, SimulationWorkflow simulation, CancellationToken ct) => simulation.Control(driverId, request.Speed, request.Paused, user.Actor(), ct)).RequireAuthorization();
        app.MapPost("/arrivals", async (ArrivalRequest request, SimulationWorkflow simulation, CancellationToken ct) => { await simulation.EnsureArrival(request.DriverId, request.TripId, request.Target, ct); return Results.NoContent(); }).RequireAuthorization("service");
    }
}
