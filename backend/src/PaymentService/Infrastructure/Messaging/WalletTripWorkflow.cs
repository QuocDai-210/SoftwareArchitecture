using MassTransit;
using SwiftRide.Application.Persistence;
using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Infrastructure;

public sealed class WalletTripWorkflow(
    IWalletAccounts wallets,
    IUnitOfWork unit,
    PaymentCommands payments
)
    : IConsumer<WalletTripHoldRequested>,
        IConsumer<WalletTripHoldReleaseRequested>,
        IConsumer<RideCompleted>
{
    public async Task Consume(ConsumeContext<WalletTripHoldRequested> context)
    {
        var message = context.Message;
        try
        {
            if (message.Amount < 0)
                throw new RuleException("Số tiền giữ ví không hợp lệ.", ErrorKind.Validation);
            await using var tx = await unit.Begin(context.CancellationToken);
            await wallets.HoldTrip(
                message.TripId,
                message.RiderId,
                message.Amount,
                context.CancellationToken,
                message.QuoteId
            );
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
            await context.Publish(
                new WalletTripHoldFailed(
                    Guid.NewGuid(),
                    message.TripId,
                    message.RiderId,
                    message.Amount,
                    ex.Message,
                    message.CorrelationId,
                    message.QuoteId
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
            context.CancellationToken,
            message.QuoteId
        );
        await unit.Save(context.CancellationToken);
        await tx.Commit(context.CancellationToken);
        await context.Publish(
            new WalletTripHoldReleased(
                Guid.NewGuid(),
                message.TripId,
                message.RiderId,
                message.Amount,
                message.CorrelationId,
                message.Rematch,
                message.CancelRequested,
                message.QuoteId
            )
        );
    }

    public async Task Consume(ConsumeContext<RideCompleted> context)
    {
        var message = context.Message;
        if (message.Method != PaymentMethod.Wallet || !message.WalletHeld)
        {
            return;
        }
        // Older deployments also route RideCompleted here. Both queues share the same
        // locked/idempotent command so payment, journal and settlement event commit together.
        await payments.Charge(
            new PaymentRequest(
                message.TripId,
                message.RiderId,
                message.Amount,
                message.Method,
                message.PaymentToken,
                message.CorrelationId,
                message.Attempt,
                message.DriverId,
                message.WalletHoldId
            ),
            $"trip:{message.TripId}:charge",
            context.CancellationToken
        );
    }
}
