using MassTransit;

namespace SwiftRide.Payments.Infrastructure;

public sealed class RideCompletedConsumer(PaymentCommands payments) : IConsumer<RideCompleted>
{
    public Task Consume(ConsumeContext<RideCompleted> context)
    {
        var m = context.Message;
        return payments.Charge(
            new(
                m.TripId,
                m.RiderId,
                m.Amount,
                m.Method,
                m.PaymentToken,
                m.CorrelationId,
                m.Attempt,
                m.DriverId,
                m.WalletHoldId
            ),
            $"trip:{m.TripId}:charge",
            context.CancellationToken
        );
    }
}
