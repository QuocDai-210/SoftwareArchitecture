using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public abstract class TripState
{
    public abstract IReadOnlySet<TripStatus> Next { get; }

    public void Ensure(TripStatus target)
    {
        if (!Next.Contains(target))
            throw new RuleException($"Không thể chuyển sang {target}.");
    }

    private sealed class Allowed(params TripStatus[] next) : TripState
    {
        public override IReadOnlySet<TripStatus> Next { get; } = next.ToHashSet();
    }

    private static readonly IReadOnlyDictionary<TripStatus, TripState> States = new Dictionary<
        TripStatus,
        TripState
    >
    {
        [TripStatus.Requested] = new Allowed(
            TripStatus.AwaitingRiderConfirmation,
            TripStatus.Cancelled,
            TripStatus.Failed
        ),
        [TripStatus.AwaitingRiderConfirmation] = new Allowed(
            TripStatus.ReservingDriver,
            TripStatus.Cancelled,
            TripStatus.Requested
        ),
        [TripStatus.ReservingDriver] = new Allowed(
            TripStatus.AwaitingDriverAcceptance,
            TripStatus.Requested,
            TripStatus.Failed
        ),
        [TripStatus.AwaitingDriverAcceptance] = new Allowed(
            TripStatus.AcceptingDriver,
            TripStatus.Requested,
            TripStatus.Cancelled,
            TripStatus.Failed
        ),
        [TripStatus.AcceptingDriver] = new Allowed(
            TripStatus.DriverAccepted,
            TripStatus.Requested,
            TripStatus.Failed
        ),
        [TripStatus.DriverAccepted] = new Allowed(TripStatus.EnRoute, TripStatus.Cancelled),
        [TripStatus.EnRoute] = new Allowed(TripStatus.PickedUp, TripStatus.Cancelled),
        [TripStatus.PickedUp] = new Allowed(TripStatus.DroppedOff),
        [TripStatus.DroppedOff] = new Allowed(TripStatus.PaymentPending),
        [TripStatus.PaymentPending] = new Allowed(TripStatus.Paid, TripStatus.PaymentReview),
        [TripStatus.PaymentReview] = new Allowed(TripStatus.PaymentPending, TripStatus.Paid),
        [TripStatus.Paid] = new Allowed(TripStatus.PaymentReview),
        [TripStatus.Cancelled] = new Allowed(),
        [TripStatus.Failed] = new Allowed(),
    };

    public static TripState For(TripStatus status) => States[status];
}
