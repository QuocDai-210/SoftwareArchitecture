namespace SwiftRide.Matching.Domain;

public static class MatchingPolicy
{
    public const double RadiusMeters = 5000;
    public static readonly TimeSpan PositionLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ReservationLifetime = TimeSpan.FromSeconds(60);
}
