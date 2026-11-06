using MassTransit;
using SwiftRide.Application.Persistence;
using Microsoft.Extensions.Logging;
using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Infrastructure;

public sealed class WalletTripWorkflow(
    IWalletAccounts wallets,
    IUnitOfWork unit,
    ILogger<WalletTripWorkflow> logger
) :
    IConsumer<WalletTripHoldRequested>,
    IConsumer<WalletTripHoldReleaseRequested>,
    IConsumer<RideCompleted>,
    IConsumer<WalletTripHoldFailed>
{
    public async Task Consume(ConsumeContext<WalletTripHoldRequested> context)
    {
        var message = context.Message;
        if (message.Amount <= 0)
            throw new RuleException("Số tiền giữ ví không hợp lệ.");
        try
        {
            await using var tx = await unit.Begin(context.CancellationToken);
            await wallets.HoldTrip(message.TripId, message.RiderId, message.Amount,
                context.CancellationToken);
            await unit.Save(context.CancellationToken);
            await tx.Commit(context.CancellationToken);
            await context.Publish(
                new WalletTripHoldSucceeded(
                    Guid.NewGuid(),
                    message.TripId,
                    message.RiderId,
                    message.Amount,
                    message.QuoteId,
                    message.TripVersion,
                    message.CorrelationId
                )
            );
        }
        catch (RuleException ex)
        {
            await using var release = await unit.Begin(context.CancellationToken);
            if (await wallets.FindActivity($"trip:{message.TripId:N}:hold", context.CancellationToken)
                is not null)
                await wallets.ReleaseTrip(message.TripId, message.RiderId, message.Amount,
                    context.CancellationToken);
            await unit.Save(context.CancellationToken);
            await release.Commit(context.CancellationToken);
            await context.Publish(
                new WalletTripHoldFailed(
                    Guid.NewGuid(),
                    message.TripId,
                    message.RiderId,
                    message.Amount,
                    ex.Message,
                    message.CorrelationId
                )
            );
        }
    }

    public async Task Consume(ConsumeContext<WalletTripHoldReleaseRequested> context)
    {
        var message = context.Message;
        await using var tx = await unit.Begin(context.CancellationToken);
        await wallets.ReleaseTrip(
            message.TripId,
            message.RiderId,
            message.Amount,
            context.CancellationToken
        );
        await unit.Save(context.CancellationToken);
        await tx.Commit(context.CancellationToken);
        await context.Publish(new WalletTripHoldReleased(
            Guid.NewGuid(), message.TripId, message.RiderId, message.Amount, message.CorrelationId
        ));
    }

    public async Task Consume(ConsumeContext<WalletTripHoldFailed> context)
    {
        var message = context.Message;
        await using var tx = await unit.Begin(context.CancellationToken);
        if (await wallets.FindActivity($"trip:{message.TripId:N}:hold", context.CancellationToken)
            is not null)
            await wallets.ReleaseTrip(message.TripId, message.RiderId, message.Amount,
                context.CancellationToken);
        await unit.Save(context.CancellationToken);
        await tx.Commit(context.CancellationToken);
        await context.Publish(new WalletTripHoldReleased(
            Guid.NewGuid(), message.TripId, message.RiderId, message.Amount, message.CorrelationId
        ));
    }

    public async Task Consume(ConsumeContext<RideCompleted> context)
    {
        var message = context.Message;
        if (message.Method != PaymentMethod.Wallet || !message.WalletHeld)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(message.DriverId))
        {
            throw new RuleException("Chuyến thanh toán ví thiếu tài xế.");
        }
        await using var tx = await unit.Begin(context.CancellationToken);
        await wallets.SettleTrip(
            message.TripId,
            message.RiderId,
            message.DriverId,
            checked((long)message.Amount),
            context.CancellationToken
        );
        await unit.Save(context.CancellationToken);
        await tx.Commit(context.CancellationToken);
        logger.LogInformation("Wallet payment settled for trip {TripId}", message.TripId);
        await context.Publish(
            new PaymentSettled(
                Guid.NewGuid(),
                message.TripId,
                message.TripId,
                message.Amount,
                "VND",
                message.CorrelationId,
                message.DriverId
            )
        );
    }
}
