using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;
using Xunit;

public sealed class WalletAccountTests
{
    private static WalletAccount Funded(long amount)
    {
        var account = new WalletAccount { OwnerId = "wallet-user" };
        account.CreditEarnings(amount);
        return account;
    }

    private static void AssertValidation(Action action)
    {
        var error = Assert.Throws<RuleException>(action);
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    private static void AssertBalances(WalletAccount account, long available, long held)
    {
        Assert.Equal(available, account.Available);
        Assert.Equal(held, account.Held);
    }

    [Theory]
    [InlineData(10000L)]
    [InlineData(250000L)]
    [InlineData(5000000L)]
    public void CreditTopUp_AmountInsideLimits_IncreasesAvailable(long amount)
    {
        var account = new WalletAccount { OwnerId = "wallet-user" };
        account.CreditTopUp(amount);
        AssertBalances(account, amount, 0);
    }

    [Fact]
    public void CreditTopUp_Repeated_AccumulatesBalance()
    {
        var account = new WalletAccount { OwnerId = "wallet-user" };
        account.CreditTopUp(10000);
        account.CreditTopUp(20000);
        AssertBalances(account, 30000, 0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-10000L)]
    [InlineData(9999L)]
    [InlineData(5000001L)]
    public void CreditTopUp_AmountOutsideLimits_IsRejectedAndBalanceUnchanged(long amount)
    {
        var account = new WalletAccount { OwnerId = "wallet-user" };
        AssertValidation(() => account.CreditTopUp(amount));
        AssertBalances(account, 0, 0);
    }

    [Fact]
    public void Hold_MovesAmountFromAvailableToHeld()
    {
        var account = Funded(100000);
        account.Hold(40000);
        AssertBalances(account, 60000, 40000);
    }

    [Fact]
    public void Hold_WholeBalance_IsAllowed()
    {
        var account = Funded(50000);
        account.Hold(50000);
        AssertBalances(account, 0, 50000);
    }

    [Fact]
    public void Hold_MoreThanAvailable_IsRejectedAndBalancesUnchanged()
    {
        var account = Funded(50000);
        AssertValidation(() => account.Hold(50001));
        AssertBalances(account, 50000, 0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Hold_NonPositiveAmount_IsRejected(long amount)
    {
        var account = Funded(50000);
        AssertValidation(() => account.Hold(amount));
        AssertBalances(account, 50000, 0);
    }

    [Theory]
    [InlineData(10000L)]
    [InlineData(5000000L)]
    public void HoldWithdrawal_AtLimits_MovesAmountToHeld(long amount)
    {
        var account = Funded(5000000);
        account.HoldWithdrawal(amount);
        AssertBalances(account, 5000000 - amount, amount);
    }

    [Theory]
    [InlineData(9999L)]
    [InlineData(5000001L)]
    public void HoldWithdrawal_OutsideLimits_IsRejectedEvenWithEnoughBalance(long amount)
    {
        var account = Funded(10000000);
        AssertValidation(() => account.HoldWithdrawal(amount));
        AssertBalances(account, 10000000, 0);
    }

    [Fact]
    public void HoldWithdrawal_MoreThanAvailable_IsRejectedAndBalancesUnchanged()
    {
        var account = Funded(20000);
        AssertValidation(() => account.HoldWithdrawal(20001));
        AssertBalances(account, 20000, 0);
    }

    [Fact]
    public void Release_ReturnsHeldAmountToAvailable()
    {
        var account = Funded(100000);
        account.Hold(60000);
        account.Release(25000);
        AssertBalances(account, 65000, 35000);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(40001L)]
    public void Release_InvalidAmount_IsRejectedAndBalancesUnchanged(long amount)
    {
        var account = Funded(100000);
        account.Hold(40000);
        Assert.Throws<RuleException>(() => account.Release(amount));
        AssertBalances(account, 60000, 40000);
    }

    [Fact]
    public void Settle_ConsumesHeldAmountWithoutReturningItToAvailable()
    {
        var account = Funded(100000);
        account.Hold(40000);
        account.Settle(40000);
        AssertBalances(account, 60000, 0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(40001L)]
    public void Settle_InvalidAmount_IsRejectedAndBalancesUnchanged(long amount)
    {
        var account = Funded(100000);
        account.Hold(40000);
        Assert.Throws<RuleException>(() => account.Settle(amount));
        AssertBalances(account, 60000, 40000);
    }

    [Fact]
    public void CompleteWithdrawal_ConsumesHeldAmount()
    {
        var account = Funded(100000);
        account.HoldWithdrawal(30000);
        account.CompleteWithdrawal(30000);
        AssertBalances(account, 70000, 0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(30001L)]
    public void CompleteWithdrawal_InvalidAmount_IsRejectedAndBalancesUnchanged(long amount)
    {
        var account = Funded(100000);
        account.HoldWithdrawal(30000);
        Assert.Throws<RuleException>(() => account.CompleteWithdrawal(amount));
        AssertBalances(account, 70000, 30000);
    }

    [Fact]
    public void CreditEarnings_PositiveAmount_IncreasesAvailableOnly()
    {
        var account = Funded(10000);
        account.CreditEarnings(5000);
        AssertBalances(account, 15000, 0);
        Assert.Equal(0, account.PendingEarnings);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void CreditEarnings_NonPositiveAmount_IsRejected(long amount)
    {
        var account = Funded(10000);
        Assert.Throws<RuleException>(() => account.CreditEarnings(amount));
        AssertBalances(account, 10000, 0);
    }

    [Fact]
    public void CreditEarnings_Overflow_ThrowsInsteadOfWrappingAround()
    {
        var account = Funded(long.MaxValue);
        Assert.Throws<OverflowException>(() => account.CreditEarnings(1));
        Assert.Equal(long.MaxValue, account.Available);
    }

    [Fact]
    public void TripPaymentFlow_HoldReleaseAndSettle_KeepsMoneyAccounted()
    {
        var account = new WalletAccount { OwnerId = "wallet-user" };
        account.CreditTopUp(100000);
        account.Hold(30000);
        account.Release(10000);
        account.Settle(20000);
        AssertBalances(account, 80000, 0);
    }
}

public sealed class WalletWithdrawalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 3, 0, 0, TimeSpan.Zero);

    private static WalletWithdrawal Pending() =>
        new()
        {
            OwnerId = "wallet-user",
            RequestKey = "request-1",
            RequestHash = "hash-1",
            Amount = 50000,
        };

    [Fact]
    public void NewWithdrawal_StartsPendingWithoutReviewData()
    {
        var withdrawal = Pending();
        Assert.Equal("Pending", withdrawal.State);
        Assert.Null(withdrawal.ReviewedAt);
        Assert.Null(withdrawal.ReviewedBy);
        Assert.Null(withdrawal.DecisionReason);
    }

    [Fact]
    public void Complete_RecordsReviewerAndTime()
    {
        var withdrawal = Pending();
        withdrawal.Complete("ops-1", Now);
        Assert.Equal("Completed", withdrawal.State);
        Assert.Equal("ops-1", withdrawal.ReviewedBy);
        Assert.Equal(Now, withdrawal.ReviewedAt);
        Assert.Null(withdrawal.DecisionReason);
    }

    [Fact]
    public void Reject_RecordsReviewerTimeAndReason()
    {
        var withdrawal = Pending();
        withdrawal.Reject("ops-1", "Thông tin tài khoản không khớp.", Now);
        Assert.Equal("Rejected", withdrawal.State);
        Assert.Equal("ops-1", withdrawal.ReviewedBy);
        Assert.Equal(Now, withdrawal.ReviewedAt);
        Assert.Equal("Thông tin tài khoản không khớp.", withdrawal.DecisionReason);
    }

    [Fact]
    public void Complete_AfterComplete_IsRejectedAndKeepsFirstReview()
    {
        var withdrawal = Pending();
        withdrawal.Complete("ops-1", Now);
        Assert.Throws<RuleException>(() => withdrawal.Complete("ops-2", Now.AddMinutes(1)));
        Assert.Equal("Completed", withdrawal.State);
        Assert.Equal("ops-1", withdrawal.ReviewedBy);
        Assert.Equal(Now, withdrawal.ReviewedAt);
    }

    [Fact]
    public void Reject_AfterComplete_IsRejectedAndStaysCompleted()
    {
        var withdrawal = Pending();
        withdrawal.Complete("ops-1", Now);
        Assert.Throws<RuleException>(() => withdrawal.Reject("ops-2", "Muộn", Now.AddMinutes(1)));
        Assert.Equal("Completed", withdrawal.State);
        Assert.Null(withdrawal.DecisionReason);
    }

    [Fact]
    public void Complete_AfterReject_IsRejectedAndStaysRejected()
    {
        var withdrawal = Pending();
        withdrawal.Reject("ops-1", "Sai thông tin", Now);
        Assert.Throws<RuleException>(() => withdrawal.Complete("ops-2", Now.AddMinutes(1)));
        Assert.Equal("Rejected", withdrawal.State);
        Assert.Equal("Sai thông tin", withdrawal.DecisionReason);
    }

    [Fact]
    public void Reject_AfterReject_IsRejectedAndKeepsFirstReason()
    {
        var withdrawal = Pending();
        withdrawal.Reject("ops-1", "Lý do một", Now);
        Assert.Throws<RuleException>(() => withdrawal.Reject("ops-2", "Lý do hai", Now));
        Assert.Equal("Lý do một", withdrawal.DecisionReason);
        Assert.Equal("ops-1", withdrawal.ReviewedBy);
    }
}
