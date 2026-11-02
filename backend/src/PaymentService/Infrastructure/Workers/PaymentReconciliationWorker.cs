using Microsoft.Extensions.Logging;

namespace SwiftRide.Payments.Infrastructure;

public sealed class PaymentReconciliationWorker(IServiceScopeFactory scopes, ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                IReadOnlyList<Guid> ids;
                try
                {
                    using var scope = scopes.CreateScope();
                    ids = await scope.ServiceProvider.GetRequiredService<ICheckoutStore>().Pending(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Payment scan deferred ({Type}).", ex.GetType().Name);
                    continue;
                }
                foreach (var id in ids)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<HostedPayments>().Reconcile(id, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogInformation("Payment {Id} reconciliation deferred ({Type}).", id, ex.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
