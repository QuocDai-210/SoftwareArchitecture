using MassTransit;
using Moq;
using SwiftRide.Application.Persistence;
using SwiftRide.Contracts;
using SwiftRide.Core;
using SwiftRide.Payments.Application;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.Payments.Infrastructure;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class ZeroFareWalletTests
{
    [Fact]
    public async Task FreeQuotePublishesHoldSuccessAfterSavingItsAuditTrail()
    {
        var message = new WalletTripHoldRequested(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "rider",
            0,
            Guid.NewGuid(),
            2,
            "test"
        );
        var accounts = new Mock<IWalletAccounts>();
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(x => x.Begin(default)).ReturnsAsync(Mock.Of<ITransaction>());
        var context = new Mock<ConsumeContext<WalletTripHoldRequested>>();
        context.SetupGet(x => x.Message).Returns(message);
        var workflow = new WalletTripWorkflow(accounts.Object, unit.Object, null!);

        await workflow.Consume(context.Object);

        accounts.Verify(
            x => x.HoldTrip(message.TripId, message.RiderId, 0, default, message.QuoteId),
            Times.Once
        );
        unit.Verify(x => x.Save(default), Times.Once);
        context.Verify(
            x =>
                x.Publish(
                    It.Is<WalletTripHoldSucceeded>(value =>
                        value.Amount == 0 && value.QuoteId == message.QuoteId
                    ),
                    default
                ),
            Times.Once
        );
    }

    [Fact]
    public void FreeTripPreservesOtherHoldsAndRejectsNegativeAmounts()
    {
        var wallet = new WalletAccount();
        wallet.CreditTopUp(100000);
        wallet.Hold(20000);
        wallet.Hold(0);
        wallet.Release(0);
        wallet.Settle(0);
        wallet.CreditEarnings(0);
        Assert.Equal(80000, wallet.Available);
        Assert.Equal(20000, wallet.Held);
        Assert.Throws<RuleException>(() => wallet.Hold(-1));
        Assert.Throws<RuleException>(() => wallet.Release(-1));
        Assert.Throws<RuleException>(() => wallet.Settle(-1));
        Assert.Throws<RuleException>(() => wallet.CreditEarnings(-1));
        Assert.Throws<RuleException>(() => wallet.CreditTopUp(0));
        Assert.Throws<RuleException>(() => wallet.HoldWithdrawal(0));
        Assert.Throws<RuleException>(() => wallet.CompleteWithdrawal(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40000)]
    public async Task WalletCaptureStillRequiresTheMatchingHold(long amount)
    {
        var accounts = new Mock<IWalletAccounts>();
        var request = new PaymentRequest(
            Guid.NewGuid(),
            "rider",
            amount,
            PaymentMethod.Wallet,
            null,
            "test",
            DriverId: "driver",
            WalletHoldId: Guid.NewGuid()
        );
        accounts
            .Setup(x =>
                x.SettleTrip(
                    request.TripId,
                    request.RiderId,
                    "driver",
                    amount,
                    default,
                    request.WalletHoldId
                )
            )
            .ThrowsAsync(new RuleException("Hold was released"));
        var method = new WalletMethod(accounts.Object);
        Assert.Equal(CaptureOutcome.Declined, (await method.Capture(request, default)).Outcome);
        accounts
            .Setup(x =>
                x.SettleTrip(
                    request.TripId,
                    request.RiderId,
                    "driver",
                    amount,
                    default,
                    request.WalletHoldId
                )
            )
            .Returns(Task.CompletedTask);
        Assert.Equal(CaptureOutcome.Settled, (await method.Capture(request, default)).Outcome);
        accounts.Verify(
            x =>
                x.SettleTrip(
                    request.TripId,
                    request.RiderId,
                    "driver",
                    amount,
                    default,
                    request.WalletHoldId
                ),
            Times.Exactly(2)
        );
    }
}
