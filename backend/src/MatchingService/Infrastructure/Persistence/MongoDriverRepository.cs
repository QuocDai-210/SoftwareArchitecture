namespace SwiftRide.Matching.Infrastructure;

public sealed class MongoDriverRepository(IMongoDatabase database)
    : IDriverReader,
        IDriverPresence,
        IDriverReservations
{
    public async Task<DriverContact?> Contact(string driverId, CancellationToken ct)
    {
        var driver = await Drivers.Find(x => x.Id == driverId).FirstOrDefaultAsync(ct);
        if (driver is null)
            return null;
        var application = await database
            .GetCollection<DriverApplication>("driverApplications")
            .Find(x => x.Subject == driverId)
            .FirstOrDefaultAsync(ct);
        return new(driver.Id, driver.Name, application?.Phone ?? "", driver.Plate, driver.Vehicle);
    }

    public Task Heartbeat(string driverId, CancellationToken ct) =>
        Drivers.UpdateOneAsync(
            x => x.Id == driverId,
            Builders<DriverDocument>.Update.Set(x => x.LastSeenAt, DateTime.UtcNow),
            cancellationToken: ct
        );

    private IMongoCollection<DriverDocument> Drivers { get; } =
        database.GetCollection<DriverDocument>("drivers");

    public async Task<DriverMatch?> Nearest(Point pickup, string[] excluded, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var f = Builders<DriverDocument>.Filter;
        var filter =
            f.Near(
                x => x.Location,
                DriverDocument.Geo(pickup.Longitude, pickup.Latitude),
                maxDistance: MatchingPolicy.RadiusMeters
            )
            & f.Eq(x => x.Available, true)
            & f.Eq(x => x.Approved, true)
            & f.Eq(x => x.Suspended, false)
            & f.Eq(x => x.Busy, false)
            & f.Gte(x => x.LastSeenAt, now.Subtract(MatchingPolicy.PositionLifetime))
            & (f.Eq(x => x.ReservationId, null) | f.Lte(x => x.LeaseExpiresAt, now))
            & f.Nin(x => x.Id, excluded);
        var driver = await Drivers.Find(filter).FirstOrDefaultAsync(ct);
        if (driver is null)
            return null;
        var meters =
            GeoDistance.Kilometers(
                pickup.Latitude,
                pickup.Longitude,
                driver.Location.Coordinates.Latitude,
                driver.Location.Coordinates.Longitude
            ) * 1000;
        return new(driver.Id, driver.Name, Math.Round(meters));
    }

    public async Task<ReservationResult> Reserve(ReservationRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var existing = await Drivers
            .Find(x =>
                x.Id == request.DriverId
                && x.TripId == request.TripId.ToString()
                && x.ReservationId == request.ReservationId.ToString()
            )
            .FirstOrDefaultAsync(ct);
        if (existing is not null && (existing.Busy || existing.LeaseExpiresAt > now))
            return new(true, existing.LeaseExpiresAt);
        var f = Builders<DriverDocument>.Filter;
        var filter =
            f.Eq(x => x.Id, request.DriverId)
            & f.Eq(x => x.Available, true)
            & f.Eq(x => x.Approved, true)
            & f.Eq(x => x.Suspended, false)
            & f.Eq(x => x.Busy, false)
            & f.Gte(x => x.LastSeenAt, now.Subtract(MatchingPolicy.PositionLifetime))
            & (f.Eq(x => x.ReservationId, null) | f.Lte(x => x.LeaseExpiresAt, now));
        var driver = await Drivers.FindOneAndUpdateAsync(
            filter,
            Builders<DriverDocument>
                .Update.Set(x => x.TripId, request.TripId.ToString())
                .Set(x => x.ReservationId, request.ReservationId.ToString())
                .Set(x => x.LeaseExpiresAt, now.Add(MatchingPolicy.ReservationLifetime)),
            new FindOneAndUpdateOptions<DriverDocument> { ReturnDocument = ReturnDocument.After },
            ct
        );
        return new(driver is not null, driver?.LeaseExpiresAt);
    }

    public async Task<ReservationResult> Confirm(ReservationRequest request, CancellationToken ct)
    {
        var f = Builders<DriverDocument>.Filter;
        var result = await Drivers.UpdateOneAsync(
            f.Eq(x => x.Id, request.DriverId)
                & f.Eq(x => x.TripId, request.TripId.ToString())
                & f.Eq(x => x.ReservationId, request.ReservationId.ToString())
                & (
                    f.Eq(x => x.Busy, true)
                    | (f.Gt(x => x.LeaseExpiresAt, DateTime.UtcNow) & f.Eq(x => x.Available, true))
                ),
            Builders<DriverDocument>.Update.Set(x => x.Busy, true).Set(x => x.LeaseExpiresAt, null),
            cancellationToken: ct
        );
        return new(result.MatchedCount == 1);
    }

    public Task Release(Guid trip, Guid? reservation, CancellationToken ct)
    {
        var filter = Builders<DriverDocument>.Filter.Eq(x => x.TripId, trip.ToString());
        if (reservation is not null)
            filter &= Builders<DriverDocument>.Filter.Eq(
                x => x.ReservationId,
                reservation.Value.ToString()
            );
        return Drivers.UpdateManyAsync(
            filter,
            Builders<DriverDocument>
                .Update.Set(x => x.Busy, false)
                .Set(x => x.TripId, null)
                .Set(x => x.ReservationId, null)
                .Set(x => x.LeaseExpiresAt, null),
            cancellationToken: ct
        );
    }

    public async Task<Driver?> Get(string driverId, CancellationToken ct)
    {
        var d = await Drivers.Find(x => x.Id == driverId).FirstOrDefaultAsync(ct);
        return d is null
            ? null
            : new(
                d.Id,
                d.Name,
                new Point(
                    d.Location.Coordinates.Latitude,
                    d.Location.Coordinates.Longitude,
                    "Driver"
                ),
                d.Available,
                d.Busy,
                d.LastSeenAt
            );
    }

    public async Task Location(string driverId, string name, Point point, CancellationToken ct)
    {
        var result = await Drivers.UpdateOneAsync(
            x =>
                x.Id == driverId
                && !x.Busy
                && x.Approved
                && !x.Suspended
                && (x.ReservationId == null || x.LeaseExpiresAt <= DateTime.UtcNow),
            Builders<DriverDocument>
                .Update.Set(x => x.Location, DriverDocument.Geo(point.Longitude, point.Latitude))
                .Set(x => x.LastSeenAt, DateTime.UtcNow),
            cancellationToken: ct
        );
        if (result.MatchedCount == 0)
            throw new RuleException("Không thể đổi vị trí khi xe đang nhận hoặc phục vụ chuyến.");
    }

    public async Task<bool> Availability(string driverId, bool available, CancellationToken ct)
    {
        var r = await Drivers.UpdateOneAsync(
            x =>
                x.Id == driverId
                && x.Approved
                && !x.Suspended
                && !x.Busy
                && (x.ReservationId == null || x.LeaseExpiresAt <= DateTime.UtcNow),
            Builders<DriverDocument>.Update.Set(x => x.Available, available),
            cancellationToken: ct
        );
        return r.MatchedCount != 0;
    }
}
