namespace SwiftRide.Application.Security;

public sealed record Actor(
    string Id,
    bool IsRider = false,
    bool IsDriver = false,
    bool IsOps = false,
    bool IsService = false
);
