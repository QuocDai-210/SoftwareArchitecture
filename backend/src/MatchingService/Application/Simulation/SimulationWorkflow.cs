using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class SimulationWorkflow(
    ISimulationStore store,
    IDriverReader drivers,
    IMapProvider maps,
    SimulationSettings settings
)
{
    public async Task Start(SwiftRide.Core.TripProgressed message, CancellationToken ct)
    {
        if (message.Stage is not ("EnRoute" or "PickedUp"))
            throw new RuleException("Chặng đi không hợp lệ.", ErrorKind.Validation);
        if (!await store.Owns(message.DriverId, message.TripId, message.ReservationId, ct))
            return;
        var previous = await store.Get(message.DriverId, ct);
        if (
            previous is not null
            && previous.TripId == message.TripId
            && previous.TripVersion >= message.Version
        )
            return;
        var driver = await drivers.Get(message.DriverId, ct)
            ?? throw new RuleException("Không có tài xế.", ErrorKind.NotFound);
        var target = message.Stage == "EnRoute" ? message.Pickup : message.Destination;
        RoadRoute route;
        if (
            GeoDistance.Kilometers(
                driver.Position.Latitude,
                driver.Position.Longitude,
                target.Latitude,
                target.Longitude
            ) < 0.05
        )
            route = new(
                0.05m,
                0.01m,
                [
                    [driver.Position.Longitude, driver.Position.Latitude],
                    [target.Longitude, target.Latitude],
                ]
            );
        else
            route =
                message.Stage == "PickedUp" && message.Route is not null
                    ? message.Route
                    : await maps.Route(driver.Position, target, ct);
        route.Validate();
        var simulation = new Simulation
        {
            DriverId = message.DriverId,
            TripId = message.TripId,
            ReservationId = message.ReservationId,
            TripVersion = message.Version,
            Stage = message.Stage,
            Route = route,
        };
        if (previous?.TripId == message.TripId)
            simulation.Control(previous.Speed, previous.Paused);
        simulation.StartAfter(previous?.Revision ?? -1);
        await store.Save(simulation, previous?.Revision, ct);
    }

    public async Task<Simulation?> Get(string driverId, Actor actor, CancellationToken ct)
    {
        if (!actor.IsService && (!actor.IsDriver || actor.Id != driverId))
            throw new RuleException("Không có quyền điều khiển xe.", ErrorKind.Forbidden);
        return await store.Get(driverId, ct);
    }

    public async Task<Simulation> Control(
        string driverId,
        int speed,
        bool paused,
        Actor actor,
        CancellationToken ct
    )
    {
        var simulation = await Get(driverId, actor, ct)
            ?? throw new RuleException("Xe chưa có hành trình.", ErrorKind.NotFound);
        var revision = simulation.Revision;
        simulation.Control(speed, paused);
        try
        {
            await store.Save(simulation, revision, ct);
        }
        catch (SimulationConflictException)
        {
            throw new RuleException("Hành trình vừa thay đổi. Hãy thử lại.");
        }
        return simulation;
    }

    public async Task Tick(CancellationToken ct)
    {
        foreach (var simulation in await store.Running(ct))
        {
            if (
                !await store.Owns(
                    simulation.DriverId,
                    simulation.TripId,
                    simulation.ReservationId,
                    ct
                )
            )
                continue;
            if (simulation.Arrived || simulation.Paused)
            {
                await store.Position(simulation, simulation.Position(), ct);
                continue;
            }
            var revision = simulation.Revision;
            simulation.Advance(1, DateTimeOffset.UtcNow);
            try
            {
                await store.Save(simulation, revision, ct);
            }
            catch (SimulationConflictException)
            {
                continue;
            }
            await store.Position(simulation, simulation.Position(), ct);
        }
    }

    public async Task EnsureArrival(
        string driverId,
        Guid tripId,
        string target,
        CancellationToken ct
    )
    {
        if (target is not ("PickedUp" or "DroppedOff"))
            throw new RuleException("Mốc hành trình không hợp lệ.", ErrorKind.Validation);
        if (!settings.EnforceArrival)
            return;
        var simulation = await store.Get(driverId, ct);
        if (
            simulation is null
            || simulation.TripId != tripId
            || !await store.Owns(driverId, tripId, simulation.ReservationId, ct)
            || simulation.Stage != (target == "PickedUp" ? "EnRoute" : "PickedUp")
            || GeoDistance.Kilometers(
                simulation.Position().Latitude,
                simulation.Position().Longitude,
                simulation.Route.Coordinates[^1][1],
                simulation.Route.Coordinates[^1][0]
            ) > 0.1
        )
            throw new RuleException("Xe chưa đến điểm đón/trả khách.");
        // Persist the position before the trip can advance or release the Driver.
        await store.Position(simulation, simulation.Position(), ct);
    }
}
