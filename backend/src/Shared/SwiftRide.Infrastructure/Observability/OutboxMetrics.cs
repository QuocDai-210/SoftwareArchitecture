using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SwiftRide.Infrastructure;

public sealed class OutboxMetrics<TDb>(
    IServiceScopeFactory scopes,
    ILogger<OutboxMetrics<TDb>> logger
) : BackgroundService
    where TDb : DbContext
{
    private long _count;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = Telemetry.Meter.CreateObservableGauge(
            "swiftride.outbox.messages",
            () => Interlocked.Read(ref _count)
        );
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TDb>();
                var sql =
                    db.Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal)
                    == true
                        ? "SELECT COUNT_BIG(*) AS [Value] FROM [OutboxMessage]"
                        : "SELECT COUNT(*) AS \"Value\" FROM \"OutboxMessage\"";
                Interlocked.Exchange(
                    ref _count,
                    await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct)
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not sample outbox depth");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
