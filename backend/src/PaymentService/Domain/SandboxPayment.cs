namespace SwiftRide.Payments.Domain;

// Sandbox is the default purpose; only explicit WalletTopUp requests may fund a wallet.
public sealed class SandboxPayment
{
    public string Reference { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public long Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string State { get; private set; } = "Pending";
    public string? ProviderTransaction { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public int CheckCount { get; set; }
    public DateTimeOffset? NextQueryAt { get; set; }
    public DateTimeOffset? LastIpnAt { get; set; }
    public string Purpose { get; init; } = "Sandbox";
    public DateTimeOffset? WalletCreditedAt { get; set; }

    public bool Record(string status, string response, string transaction, DateTimeOffset now)
    {
        if (State is "Succeeded" or "Failed")
        {
            return false;
        }
        if (status == "00" && response == "00" && transaction != "0")
        {
            State = "Succeeded";
            ProviderTransaction = transaction;
            ConfirmedAt = now;
        }
        else if (status == "02")
        {
            State = "Failed";
            ConfirmedAt = now;
        }
        else
        {
            State = "NeedsReview";
        }
        return true;
    }
}
