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
    bool RequiresCheckout
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
            p.Status == PaymentStatus.Pending && p.CheckoutReference is not null
        );
}
