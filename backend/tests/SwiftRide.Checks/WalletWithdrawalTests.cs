using Moq;
using SwiftRide.Application.Persistence;
using SwiftRide.Application.Security;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class WalletWithdrawalTests
{
    [Theory]
    [InlineData("Completed", 70000)]
    [InlineData("Rejected", 80000)]
    public async Task WorkerRecordsOneDecisionAndReleasesOnlyItsOwnHold(
        string outcome,
        long expectedAvailable
    )
    {
        var fixture = new Fixture();
        fixture.Wallet.Hold(20000);
        var result = await fixture.Commands.Request(
            new WalletWithdrawalRequest(10000),
            "withdrawal-replay-key",
            fixture.Owner,
            default
        );
        Assert.Equal(70000, fixture.Wallet.Available);
        Assert.Equal(30000, fixture.Wallet.Held);

        Assert.True(await fixture.Commands.ProcessNext(outcome, default));
        Assert.Equal(expectedAvailable, fixture.Wallet.Available);
        Assert.Equal(20000, fixture.Wallet.Held);
        Assert.Equal(outcome, fixture.Stored!.State);
        Assert.Equal(2, fixture.Journals.Count);
        Assert.Equal(2, fixture.Activities.Count);
        Assert.All(fixture.Journals, journal => Ledger.Validate(journal.Lines));
        if (outcome == "Rejected")
        {
            Assert.False(string.IsNullOrWhiteSpace(fixture.Stored.DecisionReason));
        }
        else
        {
            Assert.Contains(fixture.Journals.Last().Lines, line =>
                line.Account == "SimulatedWithdrawalClearing" && line.Amount == 10000
            );
        }

        // A second worker may have selected the same request before its first decision committed.
        Assert.True(await fixture.Commands.ProcessNext(outcome, default));
        var replay = await fixture.Commands.Request(
            new WalletWithdrawalRequest(10000),
            "withdrawal-replay-key",
            fixture.Owner,
            default
        );
        Assert.Equal(result.Id, replay.Id);
        Assert.Equal(outcome, replay.State);
        Assert.Equal(2, fixture.Journals.Count);
        Assert.Equal(20000, fixture.Wallet.Held);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Complete")]
    [InlineData("")]
    public async Task UnknownFixtureCannotCompleteAWithdrawal(string outcome)
    {
        var fixture = new Fixture();
        await fixture.Commands.Request(
            new WalletWithdrawalRequest(10000),
            "invalid-fixture-key",
            fixture.Owner,
            default
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Commands.ProcessNext(outcome, default)
        );

        Assert.Equal("Pending", fixture.Stored!.State);
        Assert.Equal(90000, fixture.Wallet.Available);
        Assert.Equal(10000, fixture.Wallet.Held);
        Assert.Single(fixture.Journals);
        Assert.Single(fixture.Activities);
    }

    [Fact]
    public async Task ReusingWithdrawalKeyWithAnotherAmountDoesNotTakeAnotherHold()
    {
        var fixture = new Fixture();
        await fixture.Commands.Request(
            new WalletWithdrawalRequest(10000),
            "withdrawal-conflict-key",
            fixture.Owner,
            default
        );

        var error = await Assert.ThrowsAsync<RuleException>(() =>
            fixture.Commands.Request(
                new WalletWithdrawalRequest(20000),
                "withdrawal-conflict-key",
                fixture.Owner,
                default
            )
        );

        Assert.Equal(ErrorKind.Conflict, error.Kind);
        Assert.Equal(90000, fixture.Wallet.Available);
        Assert.Equal(10000, fixture.Wallet.Held);
        Assert.Single(fixture.Journals);
    }

    private sealed class Fixture
    {
        public Actor Owner { get; } = new("rider001", IsRider: true);
        public WalletAccount Wallet { get; } = new() { OwnerId = "rider001" };
        public WalletWithdrawal? Stored { get; private set; }
        public List<Journal> Journals { get; } = [];
        public List<WalletActivity> Activities { get; } = [];
        public WalletWithdrawals Commands { get; }

        public Fixture()
        {
            Wallet.CreditTopUp(100000);
            var accounts = new Mock<IWalletAccounts>();
            accounts.Setup(x => x.Find(Owner.Id, default)).ReturnsAsync(Wallet);
            accounts.Setup(x => x.Add(It.IsAny<Journal>())).Callback<Journal>(Journals.Add);
            accounts
                .Setup(x => x.Add(It.IsAny<WalletActivity>()))
                .Callback<WalletActivity>(Activities.Add);
            var withdrawals = new Mock<IWalletWithdrawalStore>();
            withdrawals
                .Setup(x => x.Add(It.IsAny<WalletWithdrawal>()))
                .Callback<WalletWithdrawal>(value => Stored = value);
            withdrawals
                .Setup(x => x.FindByKey(Owner.Id, It.IsAny<string>(), default))
                .ReturnsAsync(() => Stored);
            withdrawals.Setup(x => x.NextPending(default)).ReturnsAsync(() => Stored);
            withdrawals.Setup(x => x.Find(It.IsAny<Guid>(), default)).ReturnsAsync(() => Stored);
            var unit = new Mock<IUnitOfWork>();
            unit.Setup(x => x.Begin(default)).ReturnsAsync(Mock.Of<ITransaction>());
            Commands = new WalletWithdrawals(
                accounts.Object,
                withdrawals.Object,
                unit.Object,
                TimeProvider.System
            );
        }
    }
}
