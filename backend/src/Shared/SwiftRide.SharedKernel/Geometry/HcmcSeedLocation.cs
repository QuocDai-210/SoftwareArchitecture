namespace SwiftRide.SharedKernel.Geometry;

public static class HcmcSeedLocation
{
    private static readonly (double Longitude, double Latitude)[] Boundary =
    [
        (106.62, 10.72),
        (106.65, 10.70),
        (106.68, 10.71),
        (106.70, 10.72),
        (106.71, 10.73),
        (106.73, 10.72),
        (106.76, 10.72),
        (106.78, 10.74),
        (106.81, 10.76),
        (106.82, 10.78),
        (106.81, 10.81),
        (106.79, 10.83),
        (106.76, 10.84),
        (106.72, 10.83),
        (106.69, 10.82),
        (106.66, 10.81),
        (106.64, 10.79),
        (106.62, 10.76),
    ];

    public static (double Latitude, double Longitude) Next(Random random)
    {
        var minimumLongitude = Boundary.Min(point => point.Longitude);
        var maximumLongitude = Boundary.Max(point => point.Longitude);
        var minimumLatitude = Boundary.Min(point => point.Latitude);
        var maximumLatitude = Boundary.Max(point => point.Latitude);
        while (true)
        {
            var longitude =
                minimumLongitude + random.NextDouble() * (maximumLongitude - minimumLongitude);
            var latitude =
                minimumLatitude + random.NextDouble() * (maximumLatitude - minimumLatitude);
            if (Contains(longitude, latitude))
                return (latitude, longitude);
        }
    }

    private static bool Contains(double longitude, double latitude)
    {
        var inside = false;
        for (int i = 0, j = Boundary.Length - 1; i < Boundary.Length; j = i++)
        {
            var current = Boundary[i];
            var previous = Boundary[j];
            var crosses = (current.Latitude > latitude) != (previous.Latitude > latitude);
            if (
                crosses
                && longitude
                    < (previous.Longitude - current.Longitude)
                        * (latitude - current.Latitude)
                        / (previous.Latitude - current.Latitude)
                        + current.Longitude
            )
                inside = !inside;
        }
        return inside;
    }
}
