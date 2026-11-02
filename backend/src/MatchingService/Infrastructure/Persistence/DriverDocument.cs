using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; set; } = Geo(106.7, 10.77);
    public bool Available { get; set; }
    public bool Busy { get; set; }
    public bool Approved { get; set; }
    public bool Suspended { get; set; }
    public string Plate { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public DateTime LastSeenAt { get; set; }
    public string? TripId { get; set; }
    public string? ReservationId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }

    public static GeoJsonPoint<GeoJson2DGeographicCoordinates> Geo(double lon, double lat) =>
        new(new(lon, lat));
}
