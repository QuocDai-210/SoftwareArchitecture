namespace SwiftRide.SharedKernel;

public static class GeoDistance
{
    public static double Kilometers(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB
    )
    {
        var lat = (latitudeB - latitudeA) * Math.PI / 180;
        var lon = (longitudeB - longitudeA) * Math.PI / 180;
        var a =
            Math.Pow(Math.Sin(lat / 2), 2)
            + Math.Cos(latitudeA * Math.PI / 180)
                * Math.Cos(latitudeB * Math.PI / 180)
                * Math.Pow(Math.Sin(lon / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
