using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace SwiftRide.Matching.Infrastructure;

public sealed class GeoapifyMapProvider(IHttpClientFactory clients, IConfiguration config) : IMapProvider
{
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        var key = config["Maps:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new RuleException("Chưa cấu hình GEOAPIFY_API_KEY.", ErrorKind.Unavailable);
        // This client has logging disabled and is excluded from HTTP tracing.
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "&apiKey=" + Uri.EscapeDataString(key));
        using var response = await clients.CreateClient("geoapify").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new RuleException("Dịch vụ bản đồ tạm thời chưa sẵn sàng.", ErrorKind.Unavailable);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static Point Place(JsonElement properties) => new(
        properties.GetProperty("lat").GetDouble(), properties.GetProperty("lon").GetDouble(),
        properties.GetProperty("formatted").GetString()![..Math.Min(120, properties.GetProperty("formatted").GetString()!.Length)]);

    public async Task<IReadOnlyList<Point>> Search(string text, CancellationToken ct)
    {
        var result = await Get("geocode/autocomplete?text=" + Uri.EscapeDataString(text)
            + "&filter=countrycode:vn&bias=proximity:106.7009,10.7769&lang=vi&limit=5", ct);
        return result.GetProperty("features").EnumerateArray().Select(x => Place(x.GetProperty("properties"))).ToArray();
    }

    public async Task<Point> Reverse(double latitude, double longitude, CancellationToken ct)
    {
        var result = await Get($"geocode/reverse?lat={Number(latitude)}&lon={Number(longitude)}&lang=vi&limit=1", ct);
        var features = result.GetProperty("features");
        if (features.GetArrayLength() == 0)
            return new(latitude, longitude, $"{Number(latitude)}, {Number(longitude)}");
        return Place(features[0].GetProperty("properties")) with { Latitude = latitude, Longitude = longitude };
    }

    public async Task<RoadRoute> Route(Point from, Point to, CancellationToken ct)
    {
        from.Validate(); to.Validate();
        var result = await Get($"routing?waypoints={Number(from.Latitude)},{Number(from.Longitude)}|{Number(to.Latitude)},{Number(to.Longitude)}&mode=drive&details=route_details", ct);
        var features = result.GetProperty("features");
        if (features.GetArrayLength() == 0)
            throw new RuleException("Không tìm được đường đi.", ErrorKind.Validation);
        var feature = features[0];
        var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
        var points = feature.GetProperty("geometry").GetProperty("type").GetString() == "MultiLineString"
            ? coordinates.EnumerateArray().SelectMany(line => line.EnumerateArray())
            : coordinates.EnumerateArray().AsEnumerable();
        var properties = feature.GetProperty("properties");
        var route = new RoadRoute(decimal.Round(properties.GetProperty("distance").GetDecimal() / 1000, 3),
            decimal.Round(properties.GetProperty("time").GetDecimal() / 60, 2),
            points.Select(p => new[] { p[0].GetDouble(), p[1].GetDouble() }).ToArray());
        route.Validate();
        return route;
    }
}
