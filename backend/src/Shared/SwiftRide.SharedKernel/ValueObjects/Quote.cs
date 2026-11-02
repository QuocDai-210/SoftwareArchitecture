namespace SwiftRide.SharedKernel;

public sealed record Quote(
    Guid Id,
    decimal Amount,
    string Currency,
    FareInput Breakdown,
    DateTimeOffset ExpiresAt,
    RoadRoute? Route = null
);
