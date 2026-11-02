namespace SwiftRide.Payments.Application;

public sealed record PaymentView(
    Guid Id,
    Guid TripId,
    string RiderId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    PaymentStatus Status,
    string? Reason,
    DateTimeOffset CreatedAt,
    bool IsTopUp,
    bool RequiresCheckout,
    string? DriverId,
    string? CollectedBy,
    DateTimeOffset? CollectedAt,
    string? CollectionReason,
    string? RefundedBy,
    DateTimeOffset? RefundedAt
)
{
    public static PaymentView From(Payment p) =>
        new(
            p.Id,
            p.Id,
            p.RiderId,
            p.Amount,
            "VND",
            p.Method,
            p.Status,
            p.Reason,
            p.CreatedAt,
            p.IsTopUp,
            false,
            p.DriverId,
            p.CollectedBy,
            p.CollectedAt,
            p.CollectionReason,
            p.RefundedBy,
            p.RefundedAt
        );
}
