using Moq;
using SwiftRide.Application.Messaging;
using SwiftRide.Application.Observability;
using SwiftRide.Core;
using SwiftRide.SharedKernel;
using SwiftRide.Trips.Application;
using SwiftRide.Trips.Application.Ports;
using SwiftRide.Trips.Domain;
using Xunit;

public sealed class PaymentNotificationTests
{
    [Fact]
    public async Task OldFaultCannotUndoReplayAndRefundCannotBeSettledAgain()
    {
        var trip = new Trip { Id = Guid.NewGuid() };
        trip.OfferQuote(
            "driver1",
            new(Guid.NewGuid(), 4200, "VND", new(1, 1), DateTimeOffset.UtcNow.AddMinutes(2))
        );
        trip.ConfirmQuote(trip.Quote!.Id, DateTimeOffset.UtcNow);
        trip.OfferDriver(DateTimeOffset.UtcNow.AddMinutes(1));
        trip.Move(TripStatus.AcceptingDriver);
        trip.AcceptDriver();
        trip.Move(TripStatus.EnRoute);
        trip.Move(TripStatus.PickedUp);
        trip.Move(TripStatus.DroppedOff);
        trip.Move(TripStatus.PaymentPending);
        var first = WorkflowEvents.Completed(trip);
        var reader = new Mock<ITripReader>();
        reader.Setup(r => r.Get(trip.Id, default)).ReturnsAsync(trip);
        var bus = new Mock<IEventPublisher>();
        var notifications = new TripPaymentNotifications(
            reader.Object,
            Mock.Of<ITripWriter>(),
            Mock.Of<ITripMetrics>(),
            bus.Object
        );
        await notifications.Faulted(first, default);
        Assert.Equal(TripStatus.PaymentReview, trip.Status);
        Assert.Throws<RuleException>(() => trip.Move(TripStatus.Cancelled));
        trip.Move(TripStatus.PaymentPending);
        await notifications.Faulted(first, default);
        await notifications.Failed(new(Guid.NewGuid(), trip.Id, "old", first.Attempt, ""), default);
        Assert.Equal(TripStatus.PaymentPending, trip.Status);
        var settled = new PaymentSettled(
            Guid.NewGuid(),
            trip.Id,
            trip.Id,
            4200,
            "VND",
            "",
            "driver1",
            trip.RiderId,
            trip.Method
        );
        await notifications.Settled(settled with { Currency = "USD" }, default);
        Assert.Equal(TripStatus.PaymentReview, trip.Status);
        trip.Move(TripStatus.PaymentPending);
        await notifications.Settled(settled with { RiderId = "other" }, default);
        Assert.Equal(TripStatus.PaymentReview, trip.Status);
        bus.Verify(
            b =>
                b.Publish(
                    It.Is<RefundRequested>(m => m.TripId == trip.Id && m.Amount == 4200),
                    "",
                    default
                ),
            Times.Once
        );
        await notifications.Settled(settled with { Amount = 4201 }, default);
        Assert.Equal(TripStatus.PaymentReview, trip.Status);
        bus.Verify(
            b =>
                b.Publish(
                    It.Is<RefundRequested>(m => m.TripId == trip.Id && m.Amount == 4201),
                    "",
                    default
                ),
            Times.Once
        );
        await notifications.Refunded(new(Guid.NewGuid(), trip.Id, trip.Id, 4201, ""), default);
        await notifications.Settled(settled, default);
        Assert.True(trip.Refunded);
        Assert.Equal(TripStatus.PaymentReview, trip.Status);
    }
}
