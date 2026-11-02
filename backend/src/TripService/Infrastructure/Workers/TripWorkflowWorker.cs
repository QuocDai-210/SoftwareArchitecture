namespace SwiftRide.Trips.Infrastructure;

public sealed class TripWorkflowWorker(
    IServiceScopeFactory scopes,
    ILogger<TripWorkflowWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scan = scopes.CreateAsyncScope();
                var ids = await scan
                    .ServiceProvider.GetRequiredService<ITripWorkflowQueue>()
                    .Due(DateTimeOffset.UtcNow, ct);
                foreach (var id in ids)
                {
                    try
                    {
                        await using var step = scopes.CreateAsyncScope();
                        await step.ServiceProvider.GetRequiredService<TripWorkflow>().Step(id, ct);
                    }
                    catch (DbUpdateConcurrencyException) { }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Workflow step failed for trip {TripId}", id);
                        try
                        {
                            await using var failure = scopes.CreateAsyncScope();
                            await failure
                                .ServiceProvider.GetRequiredService<TripWorkflow>()
                                .RecordFailure(id, TransientErrors.IsTransient(ex), ct);
                        }
                        catch (Exception saveError) when (!ct.IsCancellationRequested)
                        {
                            logger.LogWarning(
                                saveError,
                                "Could not persist workflow failure for {TripId}",
                                id
                            );
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Workflow scan deferred");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
