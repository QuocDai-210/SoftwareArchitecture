using MassTransit;

namespace SwiftRide.Payments.Infrastructure;

public sealed class RefundConsumer(IPaymentReader reader, PaymentCommands payments)
    : IConsumer<RefundRequested>
{
    public async Task Consume(ConsumeContext<RefundRequested> context)
    {
        var message = context.Message;
        var payment = await reader.Find(message.TripId, context.CancellationToken);
        if (payment?.Amount != message.Amount || payment.Status != PaymentStatus.Settled)
            return;
        await payments.Refund(
            payment.Id,
            $"trip:{payment.Id}:refund",
            new RefundRequest("Charge differs from the confirmed trip."),
            new Actor("trip-saga", IsService: true),
            context.CancellationToken
        );
    }
}
