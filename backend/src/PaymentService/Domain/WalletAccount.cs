namespace SwiftRide.Payments.Domain;

public sealed class WalletAccount
{
    public string OwnerId { get; init; } = "";
    public long Available { get; init; }
}
