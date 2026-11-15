using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Moq;
using SwiftRide.Matching.Application;
using SwiftRide.Matching.Application.Ports;
using SwiftRide.Matching.Domain;
using SwiftRide.Matching.Infrastructure;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class SimulationRecoveryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestartRepairsFinalPositionAndDoesNotReplayMovement()
    {
        var container = "swiftride-simulation-check-" + Guid.NewGuid().ToString("N");
        try
        {
            await Docker(
                "run",
                "--rm",
                "-d",
                "--name",
                container,
                "--memory",
                "384m",
                "--memory-swap",
                "384m",
                "-p",
                "127.0.0.1::27017",
                "mongo:8.0.8",
                "--wiredTigerCacheSizeGB",
                "0.25"
            );
            var address = (await Docker("port", container, "27017/tcp")).Trim();
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            using var client = new MongoClient(
                "mongodb://" + address + "/?serverSelectionTimeoutMS=30000"
            );
            var db = client.GetDatabase("SimulationRecoveryChecks");
            var drivers = db.GetCollection<DriverDocument>("drivers");
            var simulations = db.GetCollection<SimulationDocument>("simulations");
            var tripId = Guid.NewGuid();
            var reservationId = Guid.NewGuid();
            await drivers.InsertOneAsync(
                new DriverDocument
                {
                    Id = "active",
                    Busy = true,
                    TripId = tripId.ToString(),
                    ReservationId = reservationId.ToString(),
                    Location = DriverDocument.Geo(106.7, 10.7),
                }
            );
            await simulations.InsertManyAsync(
                Enumerable
                    .Range(0, 101)
                    .Select(i => new SimulationDocument
                    {
                        Id = "cancelled-" + i,
                        Value = new Simulation(),
                    })
            );
            var simulation = new Simulation
            {
                DriverId = "active",
                TripId = tripId,
                ReservationId = reservationId,
                Stage = "PickedUp",
                Route = new RoadRoute(
                    1,
                    0.01m,
                    [
                        [106.7, 10.7],
                        [106.71, 10.71],
                    ]
                ),
            };
            simulation.StartAfter(-1);
            var store = new MongoSimulationStore(db);
            await store.Save(simulation, null, default);
            simulation.Advance(1, DateTimeOffset.UtcNow);
            await store.Save(simulation, 0, default);
            Assert.True(simulation.Arrived);
            // Stop after the progress write, before Driver position is written.
            var restored = new MongoSimulationStore(db);
            var workflow = new SimulationWorkflow(
                restored,
                Mock.Of<IDriverReader>(),
                Mock.Of<IMapProvider>(),
                new SimulationSettings()
            );
            Assert.Single(await restored.Running(default));
            await workflow.Tick(default);
            var driver = await drivers.Find(x => x.Id == "active").SingleAsync();
            Assert.Equal(106.71, driver.Location.Coordinates.Longitude);
            Assert.Equal(10.71, driver.Location.Coordinates.Latitude);
            var repaired = (await restored.Get("active", default))!;
            Assert.Equal(simulation.Revision, repaired.Revision);
            Assert.Equal(
                simulation.UpdatedAt.ToUnixTimeMilliseconds(),
                repaired.UpdatedAt.ToUnixTimeMilliseconds()
            );
            Assert.Empty(await restored.Running(default));

            // Old acknowledgement cannot clear a newer pending position, even when paused.
            repaired.Control(20, true);
            await restored.Save(repaired, simulation.Revision, default);
            await store.Position(simulation, simulation.Position(), default);
            Assert.Single(await restored.Running(default));
            await workflow.Tick(default);
            Assert.Empty(await restored.Running(default));

            // Pre-upgrade documents have no acknowledgement field and must also be repaired.
            await db.GetCollection<BsonDocument>("simulations")
                .UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", "active"),
                    Builders<BsonDocument>.Update.Unset("PositionPending")
                );
            Assert.Single(await restored.Running(default));
            await workflow.EnsureArrival("active", tripId, "DroppedOff", default);
            Assert.Empty(await restored.Running(default));
        }
        finally
        {
            await Docker("stop", container);
        }
    }

    private static async Task<string> Docker(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
