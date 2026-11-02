namespace SwiftRide.Trips.Application;

public sealed record TripView(
    Guid Id,
    string RiderId,
    Point Pickup,
    Point Destination,
    TripStatus Status,
    string? DriverId,
    Quote? Quote,
    PaymentMethod Method,
    int Rejections,
    DateTimeOffset? Deadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Reason,
    long Version,
    IReadOnlyList<TripChange> History
)
{
    public static TripView From(Trip t) =>
        new(
            t.Id,
            t.RiderId,
            t.Pickup,
            t.Destination,
            t.Status,
            t.DriverId,
            t.Quote,
            t.Method,
            t.Rejections,
            t.Deadline,
            t.CreatedAt,
            t.UpdatedAt,
            t.Reason,
            t.Version,
            t.History
        );
}
