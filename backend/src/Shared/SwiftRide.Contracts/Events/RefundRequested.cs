namespace SwiftRide.Core;

public sealed record RefundRequested(Guid TripId, decimal Amount, string CorrelationId);
