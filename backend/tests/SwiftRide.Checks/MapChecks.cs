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
        var profile = new UserProfile { Id = "user" };
        check(Invalid(() => profile.Update("User", null!)), "profile rejects null phone");
        check(
            Invalid(() => profile.Update("User", "09+1234567")),
            "profile rejects misplaced phone prefix"
        );
        profile.Update(" User ", "+84901234567");
        check(profile.Name == "User", "profile validates and trims name");
        var walletReview = new Trip { Method = PaymentMethod.Wallet };
        walletReview.Move(TripStatus.AwaitingRiderConfirmation);
        walletReview.Move(TripStatus.WalletHoldPending);
        walletReview.Move(TripStatus.PaymentReview);
        check(
            walletReview.CanCancelWalletHold,
            "Rider can cancel a failed wallet hold before pickup"
        );
        walletReview.Move(TripStatus.AwaitingRiderConfirmation);
        walletReview.Move(TripStatus.WalletHoldPending);
        walletReview.Move(TripStatus.ReservingDriver);
        walletReview.Move(TripStatus.AwaitingDriverAcceptance);
        walletReview.Move(TripStatus.AcceptingDriver);
        walletReview.Move(TripStatus.DriverAccepted);
        walletReview.Move(TripStatus.EnRoute);
        walletReview.Move(TripStatus.PickedUp);
        walletReview.Move(TripStatus.DroppedOff);
        walletReview.Move(TripStatus.PaymentPending);
        walletReview.Move(TripStatus.PaymentReview);
        check(!walletReview.CanCancelWalletHold, "Rider cannot cancel wallet review after pickup");
        check(
            simulation.RemainingMinutes < route.DurationMinutes,
            "ETA decreases with simulated progress"
        );
        var rated = new Trip { RiderName = "User", RiderPhone = "0901234567" };
        foreach (
            var status in new[]
            {
                TripStatus.AwaitingRiderConfirmation,
                TripStatus.ReservingDriver,
                TripStatus.AwaitingDriverAcceptance,
                TripStatus.AcceptingDriver,
                TripStatus.DriverAccepted,
                TripStatus.EnRoute,
            }
        )
            rated.Move(status);
        check(Invalid(() => rated.Rate(5, "Good", rated.Version)), "cannot rate before dropoff");
        check(
            SwiftRide.Trips.Application.TripView.From(rated).RiderPhone == "0901234567",
            "assigned trip exposes rider contact while active"
        );
        rated.Move(TripStatus.PickedUp);
        check(Invalid(() => rated.Move(TripStatus.Cancelled)), "cannot cancel after pickup");
        rated.Move(TripStatus.DroppedOff);
        rated.Move(TripStatus.PaymentPending);
        check(
            SwiftRide.Trips.Application.TripView.From(rated).RiderPhone is null,
            "rider phone hidden after dropoff"
        );
        check(Invalid(() => rated.Rate(0, "", rated.Version)), "rating lower bound");
        check(Invalid(() => rated.Rate(6, "", rated.Version)), "rating upper bound");
        check(
            Invalid(() => rated.Rate(5, new string('x', 501), rated.Version)),
            "review length bound"
        );
        var beforeRating = rated.Version;
        rated.Rate(5, " Good ", beforeRating);
        rated.Rate(5, "Good", beforeRating);
        check(
            rated.Rating == 5 && rated.Review == "Good" && rated.Version == beforeRating + 1,
            "review retry is idempotent and trims comment"
        );
        check(
            Invalid(() => rated.Rate(4, "Changed", rated.Version)),
            "review cannot be replaced by duplicate command"
        );
    }
}
