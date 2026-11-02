namespace SwiftRide.Application.Messaging;

public interface IEventPublisher
{
    Task Publish<T>(T message, string correlationId, CancellationToken ct = default)
        where T : class;
}
