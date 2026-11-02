namespace SwiftRide.Contracts;

public sealed record CashCollectionRequest(bool Received, string? Reason = null);
