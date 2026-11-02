namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverDatabaseInitializer(IMongoDatabase database, IConfiguration config)
{
    private IMongoCollection<DriverDocument> Drivers { get; } =
        database.GetCollection<DriverDocument>("drivers");

    public async Task Initialize(CancellationToken ct)
    {
        await Drivers.UpdateManyAsync(Builders<DriverDocument>.Filter.Exists(x => x.Approved, false),
            Builders<DriverDocument>.Update.Set(x => x.Approved, config.GetValue<bool>("Fixtures:Enabled")).Set(x => x.Available, false), cancellationToken: ct);
        await Drivers.Indexes.CreateOneAsync(
            new CreateIndexModel<DriverDocument>(
                Builders<DriverDocument>.IndexKeys.Geo2DSphere(x => x.Location)
            ),
            cancellationToken: ct
        );
        await Drivers.Indexes.CreateOneAsync(
            new CreateIndexModel<DriverDocument>(
                Builders<DriverDocument>.IndexKeys.Ascending(x => x.TripId)
            ),
            cancellationToken: ct
        );
        if (!config.GetValue<bool>("Fixtures:Enabled")) return;
        var names = new[] { "Minh", "Lan", "Hùng", "An" };
        for (var i = 1; i <= 4; i++)
        {
            var id = $"22222222-2222-2222-2222-{i:000000000000}";
            await Drivers.UpdateOneAsync(
                x => x.Id == id,
                Builders<DriverDocument>
                    .Update.SetOnInsert(x => x.Name, names[i - 1])
                    .Set(x => x.Approved, true)
                    .SetOnInsert(x => x.Available, false)
                    .SetOnInsert(x => x.Busy, false)
                    .SetOnInsert(
                        x => x.Location,
                        DriverDocument.Geo(106.698 + i * 0.001, 10.775 + i * 0.001)
                    )
                    .SetOnInsert(x => x.LastSeenAt, DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true },
                ct
            );
        }
    }
}
