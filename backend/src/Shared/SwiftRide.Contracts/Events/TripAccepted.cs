// Preserve the deployed MassTransit message URN and exchange names.
namespace SwiftRide.Core;

public sealed record TripAccepted(
    Guid EventId,
    Guid TripId,
    string DriverId,
    Guid ReservationId,
    string CorrelationId
);
