using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class SettledConsumer(TripPaymentNotifications notifications)
    : IConsumer<PaymentSettled>,
        IConsumer<PaymentRefunded>
{
    public Task Consume(ConsumeContext<PaymentRefunded> context) =>
        notifications.Refunded(context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<PaymentSettled> context) =>
        notifications.Settled(context.Message, context.CancellationToken);
}
