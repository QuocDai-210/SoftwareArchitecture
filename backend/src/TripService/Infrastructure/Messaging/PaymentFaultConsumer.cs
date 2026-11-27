using MassTransit;

namespace SwiftRide.Trips.Infrastructure;

public sealed class PaymentFaultConsumer(TripPaymentNotifications notifications)
    : IConsumer<Fault<RideCompleted>>
{
    public Task Consume(ConsumeContext<Fault<RideCompleted>> context) =>
        notifications.Faulted(context.Message.Message, context.CancellationToken);
}
