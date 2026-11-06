using Microsoft.Extensions.Logging;
using SwiftRide.Payments.Application.Sandbox;

namespace SwiftRide.Payments.Infrastructure.Sandbox;

public sealed class SandboxReconciliation(
    IServiceScopeFactory scopes,
    SandboxVnpayOptions options,
    TimeProvider clock,
    ILogger<SandboxReconciliation> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISandboxStore>();
                var references = await store.Due(clock.GetUtcNow(), stoppingToken);
                foreach (var reference in references)
                {
                    try
                    {
                        using var item = scopes.CreateScope();
                        await item
                            .ServiceProvider.GetRequiredService<SandboxPayments>()
                            .Reconcile(reference, stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Never log provider payloads, signatures or merchant secrets.
                        logger.LogWarning(
                            "Sandbox reconciliation deferred for {Reference}",
                            reference
                        );
                    }
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Sandbox reconciliation batch deferred");
            }
        }
    }
}
