using MassTransit;

namespace SwiftRide.Matching.Infrastructure;

public sealed class DriverEvents(DriverReservations reservations, SimulationWorkflow simulation)
    : IConsumer<ReservationReleased>,
        IConsumer<RideCompleted>,
        IConsumer<TripProgressed>
{
    public Task Consume(ConsumeContext<TripProgressed> context) =>
        simulation.Start(context.Message, context.CancellationToken);

    public Task Consume(ConsumeContext<ReservationReleased> context) =>
        reservations.Release(
            context.Message.TripId,
            context.Message.ReservationId,
            context.CancellationToken
        );

    public Task Consume(ConsumeContext<RideCompleted> context) =>
        reservations.Release(context.Message.TripId, null, context.CancellationToken);
}
