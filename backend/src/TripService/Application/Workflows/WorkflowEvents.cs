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
            trip.Version,
            trip.DriverId,
            trip.Method == PaymentMethod.Wallet,
            trip.Method == PaymentMethod.Wallet ? trip.Quote?.Id : null
        );

    public static WalletTripHoldRequested ReserveWallet(Trip trip) =>
        new(
            trip.Quote!.Id,
            trip.Id,
            trip.RiderId,
            checked((long)trip.Quote!.Amount),
            trip.Quote.Id,
            trip.Version,
            trip.CorrelationId
        );

    public static WalletTripHoldReleaseRequested ReleaseWallet(
        Trip trip,
        bool rematch = false,
        bool cancelRequested = false
    ) =>
        new(
            Guid.NewGuid(),
            trip.Id,
            trip.RiderId,
            checked((long)trip.Quote!.Amount),
            trip.CorrelationId,
            rematch,
            cancelRequested,
            trip.Quote?.Id
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
