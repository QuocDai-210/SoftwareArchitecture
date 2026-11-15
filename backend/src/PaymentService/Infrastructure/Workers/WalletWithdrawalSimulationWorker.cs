namespace SwiftRide.Payments.Infrastructure;

public sealed class WalletWithdrawalSimulationWorker(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<WalletWithdrawalSimulationWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var withdrawals = scope.ServiceProvider.GetRequiredService<WalletWithdrawals>();
                var outcome = config["Wallets:WithdrawalOutcome"] ?? "Completed";
                var processed = await withdrawals.ProcessNext(outcome, ct);
                if (processed)
                    continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Wallet withdrawal simulation was deferred");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
