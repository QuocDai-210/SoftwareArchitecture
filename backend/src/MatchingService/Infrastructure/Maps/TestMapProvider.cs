namespace SwiftRide.Matching.Infrastructure;

// ponytail: deterministic adapter for isolated tests; coursework always uses Geoapify.
public sealed class TestMapProvider : IMapProvider
{
    public Task<IReadOnlyList<Point>> Search(string text, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Point>>([new(10.7769, 106.7009, text)]);

    public Task<Point> Reverse(double latitude, double longitude, CancellationToken ct) =>
        Task.FromResult(new Point(latitude, longitude, "Vị trí kiểm thử"));

    public Task<RoadRoute> Route(Point from, Point to, CancellationToken ct)
    {
        var estimate = Routes.Estimate(from, to);
        return Task.FromResult(
            new RoadRoute(
                estimate.DistanceKm,
                estimate.DurationMinutes,
                [
                    [from.Longitude, from.Latitude],
                    [to.Longitude, to.Latitude],
                ]
            )
        );
    }
}
