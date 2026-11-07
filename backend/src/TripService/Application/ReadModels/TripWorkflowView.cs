namespace SwiftRide.Trips.Application;

public sealed record TripWorkflowView(
    Guid TripId,
    TripStatus Status,
    int RetryCount,
    DateTimeOffset? NextAttemptAt,
    string? LastReason,
    string CorrelationId,
    DateTimeOffset UpdatedAt
);
