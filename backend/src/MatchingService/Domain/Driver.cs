using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed record Driver(
    string Id,
    string Name,
    Point Position,
    bool Available,
    bool Busy,
    DateTimeOffset LastSeenAt
);
