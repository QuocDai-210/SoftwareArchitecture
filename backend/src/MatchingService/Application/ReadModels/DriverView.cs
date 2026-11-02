namespace SwiftRide.Matching.Application;

public sealed record DriverView(
    string Id,
    string Name,
    bool Available,
    bool Busy,
    DateTimeOffset LastSeenAt,
    double Latitude,
    double Longitude
);
