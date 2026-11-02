namespace SwiftRide.Matching.Application.Ports;

public interface IDriverReservations
{
    Task<ReservationResult> Reserve(ReservationRequest request, CancellationToken ct);
    Task<ReservationResult> Confirm(ReservationRequest request, CancellationToken ct);
    Task Release(Guid tripId, Guid? reservationId, CancellationToken ct);
}
