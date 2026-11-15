using SwiftRide.SharedKernel.Geometry;

namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverDatabaseInitializer(IMongoDatabase database, IConfiguration config)
{
    private IMongoCollection<DriverDocument> Drivers { get; } =
        database.GetCollection<DriverDocument>("drivers");

    public async Task Initialize(CancellationToken ct)
    {
        await Drivers.UpdateManyAsync(
            Builders<DriverDocument>.Filter.Exists(x => x.Suspended, false),
            Builders<DriverDocument>.Update.Set(x => x.Suspended, false),
            cancellationToken: ct
        );
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
        if (!config.GetValue<bool>("Fixtures:Enabled"))
            return;
        for (var i = 1; i <= 1000; i++)
        {
            var random = new Random(20262114 + i);
            var id = $"22222222-2222-2222-2222-{i:000000000000}";
            var location = HcmcSeedLocation.Next(random);
            await Drivers.UpdateOneAsync(
                x => x.Id == id,
                Builders<DriverDocument>
                    .Update.SetOnInsert(x => x.Name, $"Driver {i:0000}")
                    .SetOnInsert(x => x.Approved, true)
                    .SetOnInsert(x => x.Available, true)
                    .SetOnInsert(x => x.Busy, false)
                    .SetOnInsert(x => x.Suspended, false)
                    .SetOnInsert(x => x.Plate, $"59-{i:00000}")
                    .SetOnInsert(x => x.Vehicle, "Xe máy mô phỏng")
                    .SetOnInsert(
                        x => x.Location,
                        DriverDocument.Geo(location.Longitude, location.Latitude)
                    )
                    .SetOnInsert(x => x.LastSeenAt, DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true },
                ct
            );
        }
    }
}
