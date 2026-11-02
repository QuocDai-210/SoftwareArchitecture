using SwiftRide.SharedKernel;

namespace SwiftRide.Core;

public sealed record TripProgressed(
    Guid EventId,
    Guid TripId,
    string DriverId,
    Guid ReservationId,
    long Version,
    string Stage,
    Point Pickup,
    Point Destination,
    RoadRoute? Route,
    string CorrelationId
);
