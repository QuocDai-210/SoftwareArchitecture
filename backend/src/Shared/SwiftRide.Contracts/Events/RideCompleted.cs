using SwiftRide.SharedKernel;

// Preserve the deployed MassTransit message URN and exchange names.
namespace SwiftRide.Core;

public sealed record RideCompleted(
    Guid EventId,
    Guid TripId,
    string RiderId,
    decimal Amount,
    PaymentMethod Method,
    string? PaymentToken,
    string CorrelationId,
    long Attempt = 0,
    string? DriverId = null,
    bool WalletHeld = false,
    Guid? WalletHoldId = null
);
