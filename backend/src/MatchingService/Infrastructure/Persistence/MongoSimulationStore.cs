namespace SwiftRide.Matching.Infrastructure;

public sealed class SimulationDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = "";
    public Simulation Value { get; set; } = new();
    public bool PositionPending { get; set; } = true;
}

public sealed class MongoSimulationStore(IMongoDatabase db) : ISimulationStore
{
    private readonly IMongoCollection<SimulationDocument> simulations =
        db.GetCollection<SimulationDocument>("simulations");
    private readonly IMongoCollection<DriverDocument> drivers = db.GetCollection<DriverDocument>(
        "drivers"
    );

    public async Task<Simulation?> Get(string driverId, CancellationToken ct) =>
        (await simulations.Find(x => x.Id == driverId).FirstOrDefaultAsync(ct))?.Value;

    public async Task<IReadOnlyList<Simulation>> Running(CancellationToken ct)
    {
        var busyDriverIds = await drivers.Find(x => x.Busy).Project(x => x.Id).ToListAsync(ct);
        if (busyDriverIds.Count == 0)
            return [];
        var filter = Builders<SimulationDocument>.Filter;
        var active =
            filter.In(x => x.Id, busyDriverIds)
            & (
                (filter.Lt(x => x.Value.Progress, 1) & filter.Eq(x => x.Value.Paused, false))
                | filter.Ne(x => x.PositionPending, false)
            );
        return (await simulations.Find(active).ToListAsync(ct)).Select(x => x.Value).ToArray();
    }

    public async Task Save(Simulation simulation, long? expectedRevision, CancellationToken ct)
    {
        var document = new SimulationDocument { Id = simulation.DriverId, Value = simulation };
        if (expectedRevision is null)
        {
            try
            {
                await simulations.InsertOneAsync(document, cancellationToken: ct);
            }
            catch (MongoWriteException ex)
                when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new SimulationConflictException();
            }
        }
        else
        {
            var result = await simulations.ReplaceOneAsync(
                x => x.Id == simulation.DriverId && x.Value.Revision == expectedRevision,
                document,
                cancellationToken: ct
            );
            if (result.MatchedCount != 1)
                throw new SimulationConflictException();
        }
    }

    public Task<bool> Owns(
        string driverId,
        Guid tripId,
        Guid reservationId,
        CancellationToken ct
    ) =>
        drivers
            .Find(x =>
                x.Id == driverId
                && x.TripId == tripId.ToString()
                && x.ReservationId == reservationId.ToString()
                && x.Busy
            )
            .AnyAsync(ct);

    public async Task Position(Simulation simulation, Point position, CancellationToken ct)
    {
        await drivers.UpdateOneAsync(
            x =>
                x.Id == simulation.DriverId
                && x.TripId == simulation.TripId.ToString()
                && x.ReservationId == simulation.ReservationId.ToString()
                && x.Busy,
            Builders<DriverDocument>
                .Update.Set(
                    x => x.Location,
                    DriverDocument.Geo(position.Longitude, position.Latitude)
                )
                .Set(x => x.LastSeenAt, DateTime.UtcNow),
            cancellationToken: ct
        );
        // A crash before this acknowledgement leaves the saved position eligible for retry.
        await simulations.UpdateOneAsync(
            x => x.Id == simulation.DriverId && x.Value.Revision == simulation.Revision,
            Builders<SimulationDocument>.Update.Set(x => x.PositionPending, false),
            cancellationToken: ct
        );
    }
}
