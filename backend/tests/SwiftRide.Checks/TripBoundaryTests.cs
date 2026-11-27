using Moq;
using SwiftRide.Application.Messaging;
using SwiftRide.Application.Observability;
using SwiftRide.Application.Security;
using SwiftRide.Core;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Application;
using SwiftRide.Trips.Application.Ports;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class TripBoundaryTests
{
    [Fact]
    public async Task WalletTripCanBeCancelledBeforeTheFirstQuote()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider",
            Method = PaymentMethod.Wallet,
        };
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, default)).ReturnsAsync(trip);
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>(MockBehavior.Strict);
        var commands = new TripCommands(
            reader.Object,
            writer.Object,
            bus.Object,
            Mock.Of<ITripMetrics>()
        );

        await commands.Cancel(
            trip.Id,
            new(trip.Version),
            new Actor("rider", IsRider: true),
            default
        );

        Assert.Equal(TripStatus.Cancelled, trip.Status);
        bus.VerifyNoOtherCalls();
        writer.Verify(x => x.Save(default), Times.Once);
    }

    [Fact]
    public void OnlyTheAssignedDriverCanReadAnOfferedTrip()
    {
        var trip = QuotedTrip(PaymentMethod.Wallet);
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        var driver = new Actor("driver", IsDriver: true);
        AssertForbidden(() => TripAccess.CanRead(trip, driver));
        trip.OfferDriver(DateTimeOffset.UtcNow.AddMinutes(1));

        TripAccess.CanRead(trip, driver);
        TripAccess.Driver(trip, driver);
        AssertForbidden(() => TripAccess.CanRead(trip, new Actor("other", IsDriver: true)));
        AssertForbidden(() => TripAccess.CanRead(trip, new Actor("driver", IsRider: true)));
    }

    [Fact]
    public async Task ExpiredDriverOfferReleasesItsReservationBeforeRematching()
    {
        var trip = QuotedTrip(PaymentMethod.Card);
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        trip.OfferDriver(DateTimeOffset.UtcNow.AddSeconds(-1));
        var reservation = trip.ReservationId;
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, default)).ReturnsAsync(trip);
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>();
        var workflow = new TripWorkflow(
            reader.Object,
            writer.Object,
            Mock.Of<IMatchingClient>(),
            bus.Object,
            Mock.Of<ITripMetrics>()
        );

        await workflow.Step(trip.Id, default);

        Assert.Equal(TripStatus.Requested, trip.Status);
        Assert.Contains("driver", trip.ExcludedDrivers);
        bus.Verify(
            x =>
                x.Publish(
                    It.Is<ReservationReleased>(message =>
                        message.TripId == trip.Id && message.ReservationId == reservation
                    ),
                    "",
                    default
                ),
            Times.Once
        );
        writer.Verify(x => x.Save(default), Times.Once);
    }

    private static Trip QuotedTrip(PaymentMethod method)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider",
            Method = method,
        };
        trip.OfferQuote(
            "driver",
            new Quote(
                Guid.NewGuid(),
                10000,
                "VND",
                new FareInput(1, 1),
                DateTimeOffset.UtcNow.AddMinutes(2)
            )
        );
        return trip;
    }

    private static void AssertForbidden(Action action) =>
        Assert.Equal(ErrorKind.Forbidden, Assert.Throws<RuleException>(action).Kind);
}
