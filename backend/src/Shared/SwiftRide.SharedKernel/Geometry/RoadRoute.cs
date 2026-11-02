namespace SwiftRide.SharedKernel;

public sealed record RoadRoute(decimal DistanceKm, decimal DurationMinutes, double[][] Coordinates)
{
    public void Validate()
    {
        if (DistanceKm is < 0.05m or > 100 || DurationMinutes is <= 0 or > 1440
            || Coordinates.Length is < 2 or > 30000
            || Coordinates.Any(p => p.Length != 2 || !double.IsFinite(p[0]) || !double.IsFinite(p[1])
                || p[0] is < -180 or > 180 || p[1] is < -90 or > 90))
            throw new RuleException("Tuyến đường không hợp lệ hoặc vượt 100 km.", ErrorKind.Validation);
    }
}
