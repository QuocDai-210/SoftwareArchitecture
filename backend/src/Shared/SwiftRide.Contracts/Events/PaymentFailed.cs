// Preserve the deployed MassTransit message URN and exchange names.
namespace SwiftRide.Core;

public sealed record PaymentFailed(
    Guid EventId,
    Guid TripId,
    string Reason,
    long Attempt,
    string CorrelationId
);
