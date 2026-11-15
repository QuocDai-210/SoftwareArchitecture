namespace SwiftRide.SharedKernel;

public static class Routes
{
    public static RouteEstimate Estimate(Point from, Point to)
    {
        if (from is null || to is null)
            throw new RuleException("Cần điểm đón và điểm đến.", ErrorKind.Validation);
        from.Validate();
        to.Validate();
        var distance =
            (decimal)
                GeoDistance.Kilometers(from.Latitude, from.Longitude, to.Latitude, to.Longitude)
            * 1.3m;
        if (distance < 0.05m || distance > 100m)
            throw new RuleException("Demo hỗ trợ tuyến từ 50 m đến 100 km.", ErrorKind.Validation);
        return new(decimal.Round(distance, 3), decimal.Round(distance / 25 * 60, 2));
    }
}
