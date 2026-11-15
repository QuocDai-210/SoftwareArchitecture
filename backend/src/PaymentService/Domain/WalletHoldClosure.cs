namespace SwiftRide.Payments.Domain;

// A release can arrive before a delayed hold request; remember it even when no money was held.
public sealed class WalletHoldClosure
{
    public Guid TripId { get; init; }
    public Guid QuoteId { get; init; }
    public string RiderId { get; init; } = "";
    public long Amount { get; init; }
}
