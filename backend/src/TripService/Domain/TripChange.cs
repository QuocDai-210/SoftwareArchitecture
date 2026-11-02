namespace SwiftRide.Trips.Domain;

public sealed record TripChange(TripStatus Status, DateTimeOffset At, string? Reason);
