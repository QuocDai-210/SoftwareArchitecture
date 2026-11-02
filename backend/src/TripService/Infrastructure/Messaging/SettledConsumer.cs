using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class SettledConsumer(TripPaymentNotifications notifications)
    : IConsumer<PaymentSettled>
{
    public Task Consume(ConsumeContext<PaymentSettled> context) =>
        notifications.Settled(context.Message, context.CancellationToken);
}
