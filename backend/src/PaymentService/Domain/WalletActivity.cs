namespace SwiftRide.Payments.Domain;

public sealed class WalletActivity
{
    public string Reference { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public Guid JournalId { get; init; }
    public string Kind { get; init; } = "WalletTopUp";
    public long Amount { get; init; }
    public long AvailableAfter { get; init; }
    public long HeldAfter { get; init; }
    public long PendingAfter { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
