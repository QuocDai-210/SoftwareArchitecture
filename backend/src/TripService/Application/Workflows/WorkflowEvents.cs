namespace SwiftRide.Trips.Application;

public static class WorkflowEvents
{
    public static RideCompleted Completed(Trip trip) =>
        new(
            Guid.NewGuid(),
            trip.Id,
            trip.RiderId,
            trip.Quote!.Amount,
            trip.Method,
            trip.PaymentToken,
            trip.CorrelationId,
            trip.PaymentAttempt,
            trip.DriverId
        );

    public static Task Release(Trip trip, IEventPublisher bus, CancellationToken ct) =>
        trip.DriverId is not null && trip.ReservationId is not null
            ? bus.Publish(
                new ReservationReleased(
                    Guid.NewGuid(),
                    trip.Id,
                    trip.DriverId,
                    trip.ReservationId.Value,
                    trip.CorrelationId
                ),
                trip.CorrelationId,
                ct
            )
            : Task.CompletedTask;
}
