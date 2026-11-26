using MassTransit;

namespace SwiftRide.Payments.Infrastructure;

public sealed class RideCompletedConsumer(
    PaymentCommands payments,
    ILogger<RideCompletedConsumer> logger
) : IConsumer<RideCompleted>
{
    public async Task Consume(ConsumeContext<RideCompleted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "Capture trip {TripId} attempt {Attempt} delivery {Delivery} CorrelationId={CorrelationId}",
            m.TripId,
            m.Attempt,
            context.GetRetryAttempt(),
            m.CorrelationId
        );
        var payment = await payments.Charge(
            new(
                m.TripId,
                m.RiderId,
                m.Amount,
                m.Method,
                m.PaymentToken,
                m.CorrelationId,
                m.Attempt,
                m.DriverId
            ),
            $"trip:{m.TripId}:charge",
            context.CancellationToken
        );
        logger.LogInformation(
            "Capture {Status} CorrelationId={CorrelationId}",
            payment.Status,
            m.CorrelationId
        );
    }
}
