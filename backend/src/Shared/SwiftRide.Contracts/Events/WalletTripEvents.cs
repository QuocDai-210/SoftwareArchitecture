namespace SwiftRide.Core;

public sealed record WalletTripHoldRequested(
    Guid EventId, Guid TripId, string RiderId, long Amount, Guid QuoteId, long TripVersion,
    string CorrelationId
);

public sealed record WalletTripHoldSucceeded(
    Guid EventId, Guid TripId, string RiderId, long Amount, Guid QuoteId, long TripVersion,
    string CorrelationId
);

public sealed record WalletTripHoldFailed(
    Guid EventId, Guid TripId, string RiderId, long Amount, string Reason, string CorrelationId
);

public sealed record WalletTripHoldReleaseRequested(
    Guid EventId, Guid TripId, string RiderId, long Amount, string CorrelationId
);

public sealed record WalletTripHoldReleased(
    Guid EventId, Guid TripId, string RiderId, long Amount, string CorrelationId
);
