namespace SwiftRide.Trips.Domain;

public enum TripStatus
{
    Requested,
    AwaitingRiderConfirmation,
    ReservingDriver,
    AwaitingDriverAcceptance,
    AcceptingDriver,
    DriverAccepted,
    EnRoute,
    PickedUp,
    DroppedOff,
    PaymentPending,
    PaymentReview,
    Paid,
    Cancelled,
    Failed,
}
