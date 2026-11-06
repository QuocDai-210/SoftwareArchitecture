using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class WalletHoldSucceededConsumer(TripPaymentNotifications notifications)
    : IConsumer<WalletTripHoldSucceeded>
{
    public Task Consume(ConsumeContext<WalletTripHoldSucceeded> context) =>
        notifications.WalletHoldSucceeded(context.Message, context.CancellationToken);
}
