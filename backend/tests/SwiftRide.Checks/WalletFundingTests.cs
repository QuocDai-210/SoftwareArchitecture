using Moq;
using SwiftRide.Application.Persistence;
using SwiftRide.Application.Security;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class WalletFundingTests
{
    [Fact]
    public async Task TopUpIsIdempotentAndCreatesBalancedJournal()
    {
        var activities = new Dictionary<string, WalletActivity>();
        var journals = new List<Journal>();
        WalletAccount? wallet = null;
        var accounts = new Mock<IWalletAccounts>();
        accounts
            .Setup(x => x.Lock("rider001", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        accounts
            .Setup(x => x.Find("rider001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => wallet);
        accounts
            .Setup(x => x.FindActivity(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (string reference, CancellationToken _) => activities.GetValueOrDefault(reference)
            );
        accounts
            .Setup(x => x.Add(It.IsAny<WalletAccount>()))
            .Callback<WalletAccount>(value => wallet = value);
        accounts.Setup(x => x.Add(It.IsAny<Journal>())).Callback<Journal>(journals.Add);
        accounts
            .Setup(x => x.Add(It.IsAny<WalletActivity>()))
            .Callback<WalletActivity>(value => activities.Add(value.Reference, value));

        var transaction = new MemoryTransaction();
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(x => x.Begin(It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        unit.Setup(x => x.Save(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var funding = new WalletFunding(accounts.Object, unit.Object, TimeProvider.System);
        var actor = new Actor("rider001", IsRider: true);
        var key = "funding-request-1";

        var first = await funding.TopUp(100000, key, actor, default);
        var replay = await funding.TopUp(100000, key, actor, default);

        Assert.Same(first, replay);
        Assert.Equal(100000, wallet!.Available);
        Assert.Single(journals);
        Assert.Single(activities);
        Ledger.Validate(journals.Single().Lines);
        Assert.Equal(2, transaction.Commits);
        await Assert.ThrowsAsync<RuleException>(() => funding.TopUp(200000, key, actor, default));
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(5000001)]
    public async Task TopUpRejectsAmountsOutsideLocalLimit(long amount)
    {
        var funding = new WalletFunding(
            Mock.Of<IWalletAccounts>(),
            Mock.Of<IUnitOfWork>(),
            TimeProvider.System
        );

        var error = await Assert.ThrowsAsync<RuleException>(() =>
            funding.TopUp(
                amount,
                "valid-idempotency-key",
                new Actor("rider", IsRider: true),
                default
            )
        );
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public async Task DriverCannotUseRiderOnlyTopUp()
    {
        var funding = new WalletFunding(
            Mock.Of<IWalletAccounts>(),
            Mock.Of<IUnitOfWork>(),
            TimeProvider.System
        );

        await Assert.ThrowsAsync<RuleException>(() =>
            funding.TopUp(
                10000,
                "valid-idempotency-key",
                new Actor("driver", IsDriver: true),
                default
            )
        );
    }

    private sealed class MemoryTransaction : ITransaction
    {
        public int Commits { get; private set; }

        public Task Commit(CancellationToken ct)
        {
            Commits++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
