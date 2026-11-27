namespace SwiftRide.Trips.Application;

public static class TripAccess
{
    public static void CanRead(Trip t, Actor user)
    {
        if (
            (!user.IsRider || t.RiderId != user.Id)
            && (
                !user.IsDriver
                || t.DriverId != user.Id
                || t.ReservationId is null
                || t.Status
                    is TripStatus.Requested
                        or TripStatus.AwaitingRiderConfirmation
                        or TripStatus.ReservingDriver
            )
        )
            throw new RuleException("Không có quyền xem chuyến này.", ErrorKind.Forbidden);
    }

    public static void Rider(Trip t, Actor user)
    {
        if (!user.IsRider || t.RiderId != user.Id)
            throw new RuleException("Chuyến không thuộc Rider này.", ErrorKind.Forbidden);
    }

    public static void Driver(Trip t, Actor user)
    {
        if (
            (
                !user.IsDriver
                || t.DriverId != user.Id
                || t.ReservationId is null
                || t.Status
                    is TripStatus.Requested
                        or TripStatus.AwaitingRiderConfirmation
                        or TripStatus.ReservingDriver
            )
        )
            throw new RuleException("Chuyến chưa giao cho Driver này.", ErrorKind.Forbidden);
    }
}
