namespace SwiftRide.Trips.Domain;

public enum TripStatus
{
    Requested,
    AwaitingRiderConfirmation,
    WalletHoldPending,
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
