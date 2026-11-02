using MassTransit;
using SwiftRide.Application.Messaging;

namespace SwiftRide.Infrastructure;

public sealed class MassTransitEventPublisher(IPublishEndpoint bus) : IEventPublisher
{
    public Task Publish<T>(T message, string correlationId, CancellationToken ct = default)
        where T : class => bus.Emit(message, correlationId, ct);
}
