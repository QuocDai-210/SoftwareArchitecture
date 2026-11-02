using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SwiftRide.Matching.Infrastructure;

public sealed class SimulationWorker(IServiceScopeFactory scopes, ILogger<SimulationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SimulationWorkflow>().Tick(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (RuleException) { }
            catch (Exception ex) { logger.LogWarning(ex, "Simulation tick deferred"); }
        }
    }
}
