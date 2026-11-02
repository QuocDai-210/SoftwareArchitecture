namespace SwiftRide.Contracts;

public sealed record ReservationRequest(Guid TripId, Guid ReservationId, string DriverId);
