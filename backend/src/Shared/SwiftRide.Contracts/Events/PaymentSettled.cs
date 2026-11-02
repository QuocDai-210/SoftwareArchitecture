// Preserve the deployed MassTransit message URN and exchange names.
namespace SwiftRide.Core;

public sealed record PaymentSettled(
    Guid EventId,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string CorrelationId
);
