using SwiftRide.SharedKernel;

// Preserve the deployed MassTransit message URN and exchange names.
namespace SwiftRide.Core;

public sealed record PaymentSettled(
    Guid EventId,
    Guid TripId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string CorrelationId,
    string? DriverId = null,
    string? RiderId = null,
    PaymentMethod? Method = null
);
