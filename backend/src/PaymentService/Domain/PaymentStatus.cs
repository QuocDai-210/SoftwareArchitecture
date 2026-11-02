namespace SwiftRide.Payments.Domain;

public enum PaymentStatus
{
    Pending,
    Settled,
    Declined,
    Refunded,
    Compensated,
    RefundPending,
}
