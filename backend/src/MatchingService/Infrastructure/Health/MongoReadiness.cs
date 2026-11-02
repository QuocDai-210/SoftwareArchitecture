using MongoDB.Bson;
using SwiftRide.Application.Health;

namespace SwiftRide.Matching.Infrastructure;

public sealed class MongoReadiness(IMongoDatabase database) : IReadinessProbe
{
    public async Task<bool> Ready(CancellationToken ct)
    {
        try
        {
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: ct
            );
            return true;
        }
        catch (MongoException)
        {
            return false;
        }
    }
}
