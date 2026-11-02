using SwiftRide.SharedKernel;

namespace SwiftRide.Contracts;

public sealed record MatchRequest(Point Pickup, string[] ExcludedDrivers);
