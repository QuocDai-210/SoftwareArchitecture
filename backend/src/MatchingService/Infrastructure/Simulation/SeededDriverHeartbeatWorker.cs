using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SwiftRide.Matching.Infrastructure;

public sealed class SeededDriverHeartbeatWorker(
    IMongoDatabase database,
    IConfiguration config,
    ILogger<SeededDriverHeartbeatWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue<bool>("Fixtures:Enabled"))
            return;
        var drivers = database.GetCollection<DriverDocument>("drivers");
        var ids = Enumerable
            .Range(1, 4)
            .Select(i => $"22222222-2222-2222-2222-{i:000000000000}")
            .ToArray();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await drivers.UpdateManyAsync(
                        Builders<DriverDocument>.Filter.In(x => x.Id, ids),
                        Builders<DriverDocument>.Update.Set(x => x.LastSeenAt, DateTime.UtcNow),
                        cancellationToken: ct
                    );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not refresh simulated driver heartbeats");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
    }
}
