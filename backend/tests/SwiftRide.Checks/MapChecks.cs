using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using SwiftRide.Infrastructure;
using SwiftRide.Matching.Domain;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Domain;

static class MapChecks
{
    public static void Run(Action<bool, string> check)
    {
        static bool Invalid(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (RuleException)
            {
                return true;
            }
        }
        check(
            TransientErrors.IsTransient(
                new RuleException("Maps unavailable", ErrorKind.Unavailable)
            )
                && !TransientErrors.IsTransient(
                    new RuleException("Invalid coordinates", ErrorKind.Validation)
                ),
            "temporary map failures retry without retrying invalid input"
        );
        var route = new RoadRoute(
            1,
            1,
            [
                [106.7, 10.7],
                [106.71, 10.7],
                [106.71, 10.71],
            ]
        );
        route.Validate();
        check(
            Invalid(() => (route with { Coordinates = null! }).Validate()),
            "route rejects missing geometry"
        );
        check(
            Invalid(() =>
                (
                    route with
                    {
                        Coordinates =
                        [
                            [double.NaN, 10],
                            [106, 10],
                        ],
                    }
                ).Validate()
            ),
            "route rejects non-finite geometry"
        );
        check(
            Invalid(() => (route with { DurationMinutes = 0 }).Validate()),
            "route rejects zero duration"
        );
        var simulation = new Simulation
        {
            DriverId = "driver",
            TripId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Route = route,
            Stage = "EnRoute",
        };
        simulation.StartAfter(-1);
        check(
            simulation.Speed == 10 && simulation.CurrentPosition.Longitude == 106.7,
            "simulation starts at pickup at default speed"
        );
        simulation.Advance(1, DateTimeOffset.UtcNow);
        check(
            Math.Abs(simulation.Progress - 1d / 6) < 0.00001,
            "simulation applies speed to real duration"
        );
        var progress = simulation.Progress;
        simulation.Control(20, true);
        simulation.Advance(2, DateTimeOffset.UtcNow);
        check(simulation.Progress == progress, "paused simulation does not move");
        check(Invalid(() => simulation.Control(2, false)), "simulation rejects unsupported speed");
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        var restored = BsonSerializer.Deserialize<Simulation>(simulation.ToBson());
        check(
            restored.TripId == simulation.TripId
                && restored.ReservationId == simulation.ReservationId,
            "Mongo preserves simulation identities"
        );
        check(
            restored.Progress == progress
                && restored.Speed == 20
                && restored.Paused
                && restored.Revision == simulation.Revision,
            "Mongo preserves private simulation state across restart"
        );
        restored.Control(20, false);
        restored.Advance(3600, DateTimeOffset.UtcNow);
        check(restored.Progress < 1, "restart does not fast-forward downtime");
        restored.Advance(2, DateTimeOffset.UtcNow);
        check(
            restored.Arrived
                && restored.CurrentPosition.Latitude == 10.71
                && restored.CurrentPosition.Longitude == 106.71,
            "simulation stops at final route coordinate"
        );
        var application = new DriverApplication
        {
            Username = "driver-test",
            Name = "Driver",
            Email = "driver@example.com",
            Phone = "0901234567",
            Plate = "59A-12345",
            Vehicle = "SwiftCar",
        };
        application.Validate();
        application.Provision();
        application.Approve(Guid.NewGuid().ToString());
        var saved = BsonSerializer.Deserialize<DriverApplication>(application.ToBson());
        check(
            saved.Status == "Approved" && saved.Subject == application.Subject,
            "Mongo preserves Driver approval state"
        );
        check(
            Invalid(() => saved.Reject("No")),
            "approved Driver cannot be rejected retroactively"
        );
        var profile = new UserProfile { Id = "user" };
        check(Invalid(() => profile.Update("User", null!)), "profile rejects null phone");
        check(
            Invalid(() => profile.Update("User", "09+1234567")),
            "profile rejects misplaced phone prefix"
        );
        profile.Update(" User ", "+84901234567");
        check(profile.Name == "User", "profile validates and trims name");
    }
}
