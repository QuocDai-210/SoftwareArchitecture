using MassTransit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using SwiftRide.Core;

namespace MatchingService;

public sealed class Driver
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; set; } = Geo(106.7, 10.77);
    public bool Available { get; set; }
    public bool Busy { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? TripId { get; set; }
    public string? ReservationId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public static GeoJsonPoint<GeoJson2DGeographicCoordinates> Geo(double lon, double lat) => new(new(lon, lat));
}
public sealed class DriverRepository(IMongoDatabase database)
{
    public IMongoCollection<Driver> Drivers { get; } = database.GetCollection<Driver>("drivers");
    public async Task Initialize(CancellationToken ct)
    {
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Geo2DSphere(x => x.Location)), cancellationToken: ct);
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Ascending(x => x.TripId)), cancellationToken: ct);
        var names = new[] { "Minh", "Lan", "Hùng", "An" };
        for (var i = 1; i <= 4; i++)
        {
            var id = $"22222222-2222-2222-2222-{i:000000000000}";
            await Drivers.UpdateOneAsync(x => x.Id == id, Builders<Driver>.Update
                .SetOnInsert(x => x.Name, names[i - 1]).SetOnInsert(x => x.Available, false).SetOnInsert(x => x.Busy, false)
                .SetOnInsert(x => x.Location, Driver.Geo(106.698 + i * 0.001, 10.775 + i * 0.001))
                .SetOnInsert(x => x.LastSeenAt, DateTime.UtcNow), new UpdateOptions { IsUpsert = true }, ct);
        }
    }
    public async Task<DriverMatch?> Match(MatchRequest request, CancellationToken ct)
    {
        if (request.Pickup is null || request.ExcludedDrivers is null) throw new RuleException("Thiếu dữ liệu matching.", 400);
        request.Pickup.Validate();
        if (request.ExcludedDrivers.Length > 100) throw new RuleException("Danh sách loại trừ quá dài.", 400);
        var now = DateTime.UtcNow;
        var f = Builders<Driver>.Filter;
        var filter = f.Near(x => x.Location, Driver.Geo(request.Pickup.Longitude, request.Pickup.Latitude), maxDistance: 5000) &
            f.Eq(x => x.Available, true) & f.Eq(x => x.Busy, false) & f.Gte(x => x.LastSeenAt, now.AddSeconds(-60)) &
            (f.Eq(x => x.ReservationId, null) | f.Lte(x => x.LeaseExpiresAt, now)) & f.Nin(x => x.Id, request.ExcludedDrivers);
        var driver = await Drivers.Find(filter).FirstOrDefaultAsync(ct);
        if (driver is null) return null;
        var lon = driver.Location.Coordinates.Longitude;
        var lat = driver.Location.Coordinates.Latitude;
        var deltaLat = (lat - request.Pickup.Latitude) * Math.PI / 180;
        var deltaLon = (lon - request.Pickup.Longitude) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(deltaLat / 2), 2) + Math.Cos(lat * Math.PI / 180) *
            Math.Cos(request.Pickup.Latitude * Math.PI / 180) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        var meters = 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
        return new(driver.Id, driver.Name, Math.Round(meters));
    }
    public async Task<ReservationResult> Reserve(ReservationRequest request, CancellationToken ct)
    {
        Validate(request);
        var now = DateTime.UtcNow;
        var existing = await Drivers.Find(x => x.Id == request.DriverId && x.TripId == request.TripId.ToString() && x.ReservationId == request.ReservationId.ToString()).FirstOrDefaultAsync(ct);
        if (existing is not null && (existing.Busy || existing.LeaseExpiresAt > now))
            return new(true, existing.LeaseExpiresAt);
        var f = Builders<Driver>.Filter;
        var filter = f.Eq(x => x.Id, request.DriverId) & f.Eq(x => x.Available, true) & f.Eq(x => x.Busy, false) &
            f.Gte(x => x.LastSeenAt, now.AddSeconds(-60)) & (f.Eq(x => x.ReservationId, null) | f.Lte(x => x.LeaseExpiresAt, now));
        var driver = await Drivers.FindOneAndUpdateAsync(filter, Builders<Driver>.Update
            .Set(x => x.TripId, request.TripId.ToString()).Set(x => x.ReservationId, request.ReservationId.ToString()).Set(x => x.LeaseExpiresAt, now.AddSeconds(60)),
            new FindOneAndUpdateOptions<Driver> { ReturnDocument = ReturnDocument.After }, ct);
        return new(driver is not null, driver?.LeaseExpiresAt);
    }
    public async Task<ReservationResult> Confirm(ReservationRequest request, CancellationToken ct)
    {
        Validate(request);
        var f = Builders<Driver>.Filter;
        var result = await Drivers.UpdateOneAsync(f.Eq(x => x.Id, request.DriverId) & f.Eq(x => x.TripId, request.TripId.ToString()) &
            f.Eq(x => x.ReservationId, request.ReservationId.ToString()) &
            (f.Eq(x => x.Busy, true) | (f.Gt(x => x.LeaseExpiresAt, DateTime.UtcNow) & f.Eq(x => x.Available, true))),
            Builders<Driver>.Update.Set(x => x.Busy, true).Set(x => x.LeaseExpiresAt, null), cancellationToken: ct);
        return new(result.MatchedCount == 1);
    }
    public Task Release(Guid trip, Guid? reservation, CancellationToken ct)
    {
        var filter = Builders<Driver>.Filter.Eq(x => x.TripId, trip.ToString());
        if (reservation is not null) filter &= Builders<Driver>.Filter.Eq(x => x.ReservationId, reservation.Value.ToString());
        return Drivers.UpdateManyAsync(filter, Builders<Driver>.Update.Set(x => x.Busy, false)
            .Set(x => x.TripId, null).Set(x => x.ReservationId, null).Set(x => x.LeaseExpiresAt, null), cancellationToken: ct);
    }
    private static void Validate(ReservationRequest r)
    {
        if (r.TripId == Guid.Empty || r.ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(r.DriverId))
            throw new RuleException("Reservation không hợp lệ.", 400);
    }
}
public sealed class DriverEvents(DriverRepository drivers) : IConsumer<ReservationReleased>, IConsumer<RideCompleted>
{
    public Task Consume(ConsumeContext<ReservationReleased> context) => drivers.Release(context.Message.TripId, context.Message.ReservationId, context.CancellationToken);
    public Task Consume(ConsumeContext<RideCompleted> context) => drivers.Release(context.Message.TripId, null, context.CancellationToken);
}
