using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverReservations(IDriverReservations reservations)
{
    public Task<ReservationResult> Reserve(ReservationRequest request, CancellationToken ct)
    {
        Validate(request);
        return reservations.Reserve(request, ct);
    }

    public Task<ReservationResult> Confirm(ReservationRequest request, CancellationToken ct)
    {
        Validate(request);
        return reservations.Confirm(request, ct);
    }

    public Task Release(Guid tripId, Guid? reservationId, CancellationToken ct)
    {
        if (tripId == Guid.Empty || reservationId == Guid.Empty)
            throw new RuleException("Reservation không hợp lệ.", ErrorKind.Validation);
        return reservations.Release(tripId, reservationId, ct);
    }

    private static void Validate(ReservationRequest r)
    {
        if (
            r.TripId == Guid.Empty
            || r.ReservationId == Guid.Empty
            || string.IsNullOrWhiteSpace(r.DriverId)
        )
            throw new RuleException("Reservation không hợp lệ.", ErrorKind.Validation);
    }
}
