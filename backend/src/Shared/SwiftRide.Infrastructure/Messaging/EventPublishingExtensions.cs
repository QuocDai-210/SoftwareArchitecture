using MassTransit;

namespace SwiftRide.Infrastructure;

public static class EventPublishingExtensions
{
    public static Task Emit<T>(
        this IPublishEndpoint bus,
        T message,
        string correlation,
        CancellationToken ct = default
    )
        where T : class =>
        bus.Publish(
            message,
            context =>
            {
                if (Guid.TryParse(correlation, out var id))
                    context.CorrelationId = id;
                context.Headers.Set("CorrelationId", correlation);
                context.Headers.Set("SchemaVersion", 1);
            },
            ct
        );
}
