using Moq;
using SwiftRide.Application.Messaging;
using SwiftRide.Application.Observability;
using SwiftRide.Application.Persistence;
using SwiftRide.Contracts;
using SwiftRide.Payments.Application;
using SwiftRide.Payments.Application.Ports;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class PaymentSimulationTests
{
    [Fact]
    public async Task DeclinedCodRequiresNewAttemptAndRetainsAssignedDriver()
    {
        Payment? stored = null;
        var writer = new Mock<IPaymentWriter>();
        writer
            .Setup(x => x.FindTracked(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        writer.Setup(x => x.Add(It.IsAny<Payment>())).Callback<Payment>(value => stored = value);
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(x => x.Begin(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ITransaction>());
        var method = new Mock<IPaymentMethod>();
        method.SetupGet(x => x.Method).Returns(PaymentMethod.Cod);
        method
            .SetupSequence(x =>
                x.Capture(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new CaptureResult(CaptureOutcome.Declined, "Fixture declined"))
            .ReturnsAsync(new CaptureResult(CaptureOutcome.Pending));
        var commands = new PaymentCommands(
            writer.Object,
            Mock.Of<IPaymentReader>(),
            unit.Object,
            new PaymentMethodFactory([method.Object]),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IPaymentMetrics>()
        );
        var request = new PaymentRequest(
            Guid.NewGuid(),
            "rider001",
            40000,
            PaymentMethod.Cod,
            null,
            "test",
            10,
            "driver001"
        );
        var key = $"trip:{request.TripId}:charge";

        await commands.Charge(request, key, default);
        Assert.Equal(PaymentStatus.Declined, stored!.Status);
        Assert.Equal("driver001", stored.DriverId);
        await commands.Charge(request, key, default);
        Assert.Equal(PaymentStatus.Declined, stored.Status);
        await commands.Charge(request with { Attempt = 11 }, key, default);
        Assert.Equal(PaymentStatus.Pending, stored.Status);
        Assert.Equal(11, stored.Attempt);
        method.Verify(
            x => x.Capture(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
        writer.Verify(x => x.Add(It.IsAny<Payment>()), Times.Once);
        writer.Verify(x => x.Add(It.IsAny<Journal>()), Times.Never);
    }

    [Fact]
    public async Task TimeoutFixtureDoesNotReportPaymentSuccess()
    {
        var method = new CashMethod(new PaymentSimulationSettings("Timeout"));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            method.Capture(
                new PaymentRequest(
                    Guid.NewGuid(),
                    "rider001",
                    10000,
                    PaymentMethod.Cod,
                    null,
                    "test"
                ),
                default
            )
        );
    }
}
