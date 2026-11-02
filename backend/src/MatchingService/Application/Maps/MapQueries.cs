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

    public Task<RoadRoute> Route(QuoteRequest request, CancellationToken ct)
    {
        if (request.Pickup is null || request.Destination is null)
            throw new RuleException("Cần điểm đón và điểm đến.", ErrorKind.Validation);
        request.Pickup.Validate();
        request.Destination.Validate();
        if (
            request.Pickup.Latitude == request.Destination.Latitude
            && request.Pickup.Longitude == request.Destination.Longitude
        )
            throw new RuleException("Điểm đón và điểm đến phải khác nhau.", ErrorKind.Validation);
        return maps.Route(request.Pickup, request.Destination, ct);
    }

    public Task<Point> Reverse(double latitude, double longitude, CancellationToken ct)
    {
        new Point(latitude, longitude, "Vị trí").Validate();
        return maps.Reverse(latitude, longitude, ct);
    }
}
