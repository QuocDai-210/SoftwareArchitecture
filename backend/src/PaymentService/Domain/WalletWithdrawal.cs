using SwiftRide.SharedKernel;

namespace SwiftRide.Payments.Domain;

public sealed class WalletWithdrawal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; init; } = "";
    public string RequestKey { get; init; } = "";
    public string RequestHash { get; init; } = "";
    public long Amount
    {
        get; init;
    }
    public string State { get; private set; } = "Pending";
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt
    {
        get; private set;
    }
    public string? ReviewedBy
    {
        get; private set;
    }
    public string? DecisionReason
    {
        get; private set;
    }

    public void Complete(string reviewer, DateTimeOffset at)
    {
        EnsurePending();
        State = "Completed";
        ReviewedBy = reviewer;
        ReviewedAt = at;
    }

    public void Reject(string reviewer, string reason, DateTimeOffset at)
    {
        EnsurePending();
        State = "Rejected";
        ReviewedBy = reviewer;
        ReviewedAt = at;
        DecisionReason = reason;
    }

    private void EnsurePending()
    {
        if (State != "Pending")
            throw new RuleException("Yêu cầu rút tiền đã được xử lý.");
    }
}
