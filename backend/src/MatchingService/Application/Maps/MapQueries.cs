using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class MapQueries(IMapProvider maps)
{
    public Task<IReadOnlyList<Point>> Search(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length is < 3 or > 120)
            throw new RuleException("Địa chỉ cần từ 3 đến 120 ký tự.", ErrorKind.Validation);
        return maps.Search(text.Trim(), ct);
    }

    public Task<Point> Reverse(double latitude, double longitude, CancellationToken ct)
    {
        new Point(latitude, longitude, "Vị trí").Validate();
        return maps.Reverse(latitude, longitude, ct);
    }
}
