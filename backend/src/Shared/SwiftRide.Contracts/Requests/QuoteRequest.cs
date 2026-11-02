using SwiftRide.SharedKernel;

namespace SwiftRide.Contracts;

public sealed record QuoteRequest(Point Pickup, Point Destination);
