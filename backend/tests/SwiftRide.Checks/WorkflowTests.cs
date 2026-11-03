using Moq;
using SwiftRide.Application.Messaging;
using SwiftRide.Application.Observability;
using SwiftRide.Application.Security;
using SwiftRide.Contracts;
using SwiftRide.Core;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Application;
using SwiftRide.Trips.Application.Ports;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class WorkflowTests
{
    [Fact]
    public async Task CancellationBeforePickupRequiresOwnerReasonAndReleasesOnlyOwnReservation()
    {
        var trip = new Trip { Id = Guid.NewGuid(), RiderId = "user" };
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
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        trip.OfferDriver(DateTimeOffset.UtcNow.AddMinutes(1));
        trip.Move(TripStatus.AcceptingDriver);
        trip.AcceptDriver();
        trip.Move(TripStatus.EnRoute);
        var reader = new Mock<ITripReader>();
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var commands = new TripCommands(
            reader.Object,
            writer.Object,
            bus.Object,
            Mock.Of<ITripMetrics>(),
            Mock.Of<IMatchingClient>(),
            Mock.Of<IUserProfileStore>()
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            commands.Cancel(
                trip.Id,
                new(trip.Version, "Changed plans"),
                new Actor("other", IsRider: true),
                default
            )
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            commands.Cancel(
                trip.Id,
                new(trip.Version),
                new Actor("driver", IsDriver: true),
                default
            )
        );
        var version = trip.Version;
        await commands.Cancel(
            trip.Id,
            new(version, "Vehicle issue"),
            new Actor("driver", IsDriver: true),
            default
        );
        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.Contains("Tài xế", trip.Reason);
        await Assert.ThrowsAsync<RuleException>(() =>
            commands.Cancel(
                trip.Id,
                new(version, "Vehicle issue"),
                new Actor("driver", IsDriver: true),
                default
            )
        );
        bus.Verify(
            x =>
                x.Publish(
                    It.Is<ReservationReleased>(e =>
                        e.TripId == trip.Id && e.ReservationId == trip.ReservationId
                    ),
                    "",
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        writer.Verify(x => x.Save(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SagaResumesAndPublishesOnlyAfterDriverReservationIsConfirmed()
    {
        var trip = new Trip { Id = Guid.NewGuid(), RiderId = "user" };
        var reader = new Mock<ITripReader>(MockBehavior.Strict);
        var writer = new Mock<ITripWriter>(MockBehavior.Strict);
        var matching = new Mock<IMatchingClient>(MockBehavior.Strict);
        var bus = new Mock<IEventPublisher>(MockBehavior.Strict);
        var metrics = new Mock<ITripMetrics>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        writer.Setup(x => x.Save(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        matching
            .Setup(x => x.Match(It.IsAny<MatchRequest>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MatchResponse(new DriverMatch("driver", "Driver", 100)));
        matching
            .Setup(x => x.Quote(It.IsAny<QuoteRequest>(), "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new Quote(
                    Guid.NewGuid(),
                    91575,
                    "VND",
                    new FareInput(5, 10),
                    DateTimeOffset.UtcNow.AddMinutes(2)
                )
            );
        var workflow = new TripWorkflow(
            reader.Object,
            writer.Object,
            matching.Object,
            bus.Object,
            metrics.Object
        );
        await workflow.Step(trip.Id, default);
        Assert.Equal(TripStatus.AwaitingRiderConfirmation, trip.Status);
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        matching
            .Setup(x =>
                x.Reserve(
                    It.Is<ReservationRequest>(r =>
                        r.TripId == trip.Id && r.ReservationId == trip.ReservationId
                    ),
                    "",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new ReservationResult(true, DateTimeOffset.UtcNow.AddSeconds(60)));
        await workflow.Step(trip.Id, default);
        Assert.Equal(TripStatus.AwaitingDriverAcceptance, trip.Status);
        bus.VerifyNoOtherCalls();
        trip.Move(TripStatus.AcceptingDriver);
        matching
            .Setup(x =>
                x.Confirm(It.IsAny<ReservationRequest>(), "", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ReservationResult(true));
        bus.Setup(x =>
                x.Publish(
                    It.Is<TripAccepted>(e =>
                        e.TripId == trip.Id && e.ReservationId == trip.ReservationId
                    ),
                    "",
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);
        await workflow.Step(trip.Id, default);
        Assert.Equal(TripStatus.DriverAccepted, trip.Status);
        bus.Verify(
            x => x.Publish(It.IsAny<TripAccepted>(), "", It.IsAny<CancellationToken>()),
            Times.Once
        );
        writer.Verify(x => x.Save(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SagaRetriesTransientFailureThenCompensatesExactlyOnce()
    {
        var trip = new Trip { Id = Guid.NewGuid() };
        trip.OfferQuote(
            "driver",
            new Quote(
                Guid.NewGuid(),
                1,
                "VND",
                new FareInput(1, 1),
                DateTimeOffset.UtcNow.AddMinutes(2)
            )
        );
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        var reader = new Mock<ITripReader>();
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var workflow = new TripWorkflow(
            reader.Object,
            writer.Object,
            Mock.Of<IMatchingClient>(),
            bus.Object,
            Mock.Of<ITripMetrics>()
        );
        for (var i = 0; i < 3; i++)
        {
            await workflow.RecordFailure(trip.Id, true, default);
            Assert.Equal(TripStatus.ReservingDriver, trip.Status);
            Assert.NotNull(trip.NextAttemptAt);
        }
        bus.VerifyNoOtherCalls();
        await workflow.RecordFailure(trip.Id, true, default);
        await workflow.RecordFailure(trip.Id, true, default);
        Assert.Equal(TripStatus.Failed, trip.Status);
        bus.Verify(
            x =>
                x.Publish(
                    It.Is<ReservationReleased>(e =>
                        e.TripId == trip.Id && e.ReservationId == trip.ReservationId
                    ),
                    "",
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }
}
