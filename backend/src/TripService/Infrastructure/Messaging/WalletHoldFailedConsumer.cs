using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class WalletHoldFailedConsumer(TripPaymentNotifications notifications)
    : IConsumer<WalletTripHoldFailed>, IConsumer<WalletTripHoldReleased>
{
    public Task Consume(ConsumeContext<WalletTripHoldFailed> context) =>
        notifications.WalletHoldFailed(context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<WalletTripHoldReleased> context) =>
        notifications.WalletHoldReleased(context.Message, context.CancellationToken);
}
