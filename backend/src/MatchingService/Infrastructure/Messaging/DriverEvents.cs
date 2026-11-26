using MassTransit;

namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverEvents(DriverReservations reservations) : IConsumer<ReservationReleased>
{
    public Task Consume(ConsumeContext<ReservationReleased> context) =>
        reservations.Release(
            context.Message.TripId,
            context.Message.ReservationId,
            context.CancellationToken
        );
}
