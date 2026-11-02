using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class FailedConsumer(TripPaymentNotifications notifications)
    : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) =>
        notifications.Failed(context.Message, context.CancellationToken);
}
