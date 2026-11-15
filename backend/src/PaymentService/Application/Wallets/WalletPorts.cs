namespace SwiftRide.Payments.Application.Wallets;

public interface IWalletAccounts
{
    Task Lock(string owner, CancellationToken ct);
    Task<WalletAccount?> Find(string owner, CancellationToken ct);
    Task<WalletActivity?> FindActivity(string reference, CancellationToken ct);
    Task<PageResult<WalletActivity>> History(string owner, int page, CancellationToken ct);
    Task<PageResult<WalletActivity>> TopUps(string owner, int page, CancellationToken ct);
    Task HoldTrip(
        Guid tripId,
        string riderId,
        long amount,
        CancellationToken ct,
        Guid? quoteId = null
    );
    Task ReleaseTrip(
        Guid tripId,
        string riderId,
        long amount,
        CancellationToken ct,
        Guid? quoteId = null
    );
    Task SettleTrip(
        Guid tripId,
        string riderId,
        string driverId,
        long amount,
        CancellationToken ct,
        Guid? quoteId = null
    );
    void Add(WalletAccount account);
    void Add(WalletActivity activity);
    void Add(Journal journal);
}

public interface IWalletFunding
{
    Task<WalletActivity> TopUp(long amount, string key, Actor actor, CancellationToken ct);
}

public interface IWalletWithdrawalStore
{
    Task<WalletWithdrawal?> Find(Guid id, CancellationToken ct);
    Task<WalletWithdrawal?> FindByKey(string owner, string key, CancellationToken ct);
    Task<PageResult<WalletWithdrawal>> History(string owner, int page, CancellationToken ct);
    Task<WalletWithdrawal?> NextPending(CancellationToken ct);
    void Add(WalletWithdrawal withdrawal);
}

public sealed record WalletView(
    long Available,
    long Held,
    long PendingEarnings,
    string Currency,
    bool CanTopUp,
    bool CanWithdraw
);
