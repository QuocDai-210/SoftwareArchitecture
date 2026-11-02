namespace SwiftRide.Contracts;

public sealed record ReservationResult(bool Acquired, DateTimeOffset? ExpiresAt = null);
