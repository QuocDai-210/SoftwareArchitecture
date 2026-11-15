using Moq;
using SwiftRide.Application.Messaging;
using SwiftRide.Application.Observability;
using SwiftRide.Core;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Application;
using SwiftRide.Trips.Application.Ports;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class WalletHoldRecoveryTests
{
    [Fact]
    public async Task ConfirmWalletPersistsHoldRequestWithState()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider001",
            Method = PaymentMethod.Wallet,
        };
        var quote = new Quote(
            Guid.NewGuid(),
            40000,
            "VND",
            new FareInput(3, 5),
            DateTimeOffset.UtcNow.AddMinutes(2)
        );
        trip.OfferQuote("driver001", quote);
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        WalletTripHoldRequested? staged = null;
        var bus = new Mock<IEventPublisher>();
        bus.Setup(x =>
                x.Publish(
                    It.IsAny<WalletTripHoldRequested>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<WalletTripHoldRequested, string, CancellationToken>(
                (message, _, _) => staged = message
            )
            .Returns(Task.CompletedTask);
        var writer = new Mock<ITripWriter>();
        writer
            .Setup(x => x.Save(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.Equal(TripStatus.WalletHoldPending, trip.Status);
                Assert.NotNull(staged);
                Assert.Equal(quote.Id, staged.QuoteId);
                Assert.Equal(trip.Id, staged.TripId);
            })
            .Returns(Task.CompletedTask);
        var commands = new TripCommands(
            reader.Object,
            writer.Object,
            bus.Object,
            Mock.Of<ITripMetrics>(),
            Mock.Of<IMatchingClient>(),
            Mock.Of<IUserProfileStore>()
        );

        await commands.ConfirmQuote(
            trip.Id,
            new(trip.Version, quote.Id),
            new SwiftRide.Application.Security.Actor(trip.RiderId, IsRider: true),
            default
        );

        writer.Verify(x => x.Save(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaleQuoteNotificationsCannotReleaseCurrentHold()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider001",
            Method = PaymentMethod.Wallet,
        };
        var quote = new Quote(
            Guid.NewGuid(),
            40000,
            "VND",
            new FareInput(3, 5),
            DateTimeOffset.UtcNow.AddMinutes(2)
        );
        trip.OfferQuote("driver001", quote);
        trip.BeginWalletHold(quote.Id, DateTimeOffset.UtcNow);
        Assert.Equal(quote.Id, WorkflowEvents.ReserveWallet(trip).EventId);
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>();
        var notifications = new TripPaymentNotifications(
            reader.Object,
            writer.Object,
            Mock.Of<ITripMetrics>(),
            Mock.Of<IPaymentMetrics>(),
            bus.Object
        );
        var previousQuote = Guid.NewGuid();

        await notifications.WalletHoldSucceeded(
            new WalletTripHoldSucceeded(
                Guid.NewGuid(),
                trip.Id,
                trip.RiderId,
                40000,
                previousQuote,
                2,
                "test"
            ),
            default
        );
        await notifications.WalletHoldFailed(
            new WalletTripHoldFailed(
                Guid.NewGuid(),
                trip.Id,
                trip.RiderId,
                40000,
                "Old failure",
                "test",
                previousQuote
            ),
            default
        );
        await notifications.WalletHoldReleased(
            new WalletTripHoldReleased(
                Guid.NewGuid(),
                trip.Id,
                trip.RiderId,
                40000,
                "test",
                QuoteId: previousQuote
            ),
            default
        );

        Assert.Equal(TripStatus.WalletHoldPending, trip.Status);
        writer.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, TripStatus.AwaitingDriverAcceptance)]
    [InlineData(true, TripStatus.AcceptingDriver)]
    public async Task AutomaticAcceptanceIsExplicitAndUsesReservationWorkflow(
        bool enabled,
        TripStatus expected
    )
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider001",
            Method = PaymentMethod.Cod,
        };
        var quote = new Quote(
            Guid.NewGuid(),
            40000,
            "VND",
            new FareInput(3, 5),
            DateTimeOffset.UtcNow.AddMinutes(2)
        );
        trip.OfferQuote("driver001", quote);
        trip.ConfirmQuote(quote.Id, DateTimeOffset.UtcNow);
        trip.OfferDriver(DateTimeOffset.UtcNow.AddSeconds(60));
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var workflow = new TripWorkflow(
            reader.Object,
            Mock.Of<ITripWriter>(),
            Mock.Of<IMatchingClient>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ITripMetrics>(),
            new TripSimulationSettings(enabled)
        );

        await workflow.Step(trip.Id, default);

        Assert.Equal(expected, trip.Status);
    }

    [Fact]
    public async Task FailedHoldRequestsReleaseAfterRecordingFailureAndIgnoresReplay()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = "rider099",
            Method = PaymentMethod.Wallet,
        };
        var quote = new Quote(
            Guid.NewGuid(),
            40000,
            "VND",
            new FareInput(3, 5),
            DateTimeOffset.UtcNow.AddMinutes(2)
        );
        trip.OfferQuote("driver001", quote);
        trip.BeginWalletHold(quote.Id, DateTimeOffset.UtcNow);
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.Get(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var writer = new Mock<ITripWriter>();
        var bus = new Mock<IEventPublisher>();
        bus.Setup(x =>
                x.Publish(
                    It.IsAny<WalletTripHoldReleaseRequested>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => Assert.Equal(TripStatus.PaymentReview, trip.Status))
            .Returns(Task.CompletedTask);
        var notifications = new TripPaymentNotifications(
            reader.Object,
            writer.Object,
            Mock.Of<ITripMetrics>(),
            Mock.Of<IPaymentMetrics>(),
            bus.Object
        );
        var failed = new WalletTripHoldFailed(
            Guid.NewGuid(),
            trip.Id,
            trip.RiderId,
            40000,
            "Insufficient funds",
            trip.CorrelationId
        );

        await notifications.WalletHoldFailed(failed, default);
        await notifications.WalletHoldFailed(failed, default);
        bus.Verify(
            x =>
                x.Publish(
                    It.IsAny<WalletTripHoldReleaseRequested>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        writer.Verify(x => x.Save(It.IsAny<CancellationToken>()), Times.Once);

        await notifications.WalletHoldReleased(
            new WalletTripHoldReleased(
                Guid.NewGuid(),
                trip.Id,
                trip.RiderId,
                40000,
                trip.CorrelationId
            ),
            default
        );
        Assert.Equal(TripStatus.AwaitingRiderConfirmation, trip.Status);
        await notifications.WalletHoldFailed(failed, default);
        Assert.Equal(TripStatus.AwaitingRiderConfirmation, trip.Status);
    }
}
