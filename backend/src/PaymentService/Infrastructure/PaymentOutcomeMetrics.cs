using System.Diagnostics.Metrics;

namespace SwiftRide.Payments.Infrastructure;

public sealed class PaymentOutcomeMetrics(
    IServiceScopeFactory scopes,
    ILogger<PaymentOutcomeMetrics> logger
) : BackgroundService
{
    private Measurement<long>[] counts = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Telemetry.Meter.CreateObservableCounter(
            "swiftride.payments.outcomes",
            () => Volatile.Read(ref counts).AsEnumerable()
        );
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PayDb>();
                // A refund reverses a successful capture. Each committed payment contributes once.
                var rows = await db
                    .Payments.GroupBy(p => p.Status == PaymentStatus.Declined)
                    .Select(g => new { Declined = g.Key, Count = g.LongCount() })
                    .ToListAsync(ct);
                Volatile.Write(
                    ref counts,
                    rows.Select(r => new Measurement<long>(
                            r.Count,
                            new KeyValuePair<string, object?>(
                                "status",
                                r.Declined ? "declined" : "settled"
                            )
                        ))
                        .ToArray()
                );
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not read payment outcomes");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
