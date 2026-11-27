using SwiftRide.SharedKernel;

namespace SwiftRide.Payments.Domain;

public sealed class Payment
{
    public Guid Id { get; set; }
    public string RiderId { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; private set; }
    public string RequestHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string? RefundKey { get; private set; }
    public string? RefundHash { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Attempt { get; private set; }
    public string? DriverId { get; private set; }
    public string? RefundedBy { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    public static Payment Captured(
        Guid id,
        string riderId,
        decimal amount,
        PaymentMethod method,
        string hash,
        string key,
        string correlation,
        bool success,
        string? reason,
        string? driverId,
        long attempt
    ) =>
        new()
        {
            Id = id,
            RiderId = riderId,
            DriverId = driverId,
            Attempt = attempt,
            Amount = amount,
            Method = method,
            RequestHash = hash,
            IdempotencyKey = key,
            CorrelationId = correlation,
            Status = success ? PaymentStatus.Settled : PaymentStatus.Declined,
            Reason = reason,
        };

    public void Refund(string key, string hash, string reason, string? subject = null)
    {
        EnsureSettled();
        Status = PaymentStatus.Refunded;
        RefundKey = key;
        RefundHash = hash;
        Reason = reason;
        RefundedBy = subject;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureSettled()
    {
        if (Status != PaymentStatus.Settled)
            throw new RuleException("Chỉ hoàn khoản đã thanh toán.");
    }
}
