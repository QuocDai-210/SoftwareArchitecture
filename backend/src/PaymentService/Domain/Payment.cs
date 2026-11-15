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
    public bool IsTopUp { get; private set; }
    public string? CheckoutReference { get; private set; }
    public long Attempt { get; private set; }
    public string? DriverId { get; private set; }
    public string? CollectionKey { get; private set; }
    public string? CollectionHash { get; private set; }
    public string? CollectedBy { get; private set; }
    public DateTimeOffset? CollectedAt { get; private set; }
    public string? CollectionReason { get; private set; }
    public string? RefundedBy { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    public void AwaitCash(string driverId, long attempt = 0)
    {
        if (
            Method != PaymentMethod.Cod
            || string.IsNullOrWhiteSpace(driverId)
            || driverId.Length > 100
            || Status is not (PaymentStatus.Declined or PaymentStatus.Pending)
            || CollectedAt is not null
        )
            throw new RuleException("Giao dịch COD cần tài xế được phân công.");
        DriverId = driverId;
        Attempt = attempt;
        Status = PaymentStatus.Pending;
        Reason = null;
    }

    public bool CollectCash(string key, string hash, string subject, string? reason)
    {
        if (Method != PaymentMethod.Cod || DriverId is null)
            throw new RuleException("Giao dịch không hỗ trợ thu COD.");
        if (CollectedAt is not null)
        {
            if (CollectionKey != key)
                throw new RuleException("Giao dịch đã được xác nhận thu tiền.");
            RequestKeys.Same(CollectionHash!, hash);
            return false;
        }
        Settle();
        CollectionKey = key;
        CollectionHash = hash;
        CollectedBy = subject;
        CollectedAt = DateTimeOffset.UtcNow;
        CollectionReason = reason;
        return true;
    }

    public bool Settle()
    {
        if (Status == PaymentStatus.Settled)
            return false;
        if (Status != PaymentStatus.Pending)
            throw new RuleException("Giao dịch không chờ thanh toán.");
        Status = PaymentStatus.Settled;
        Reason = null;
        return true;
    }

    public void Decline(string reason)
    {
        if (Status == PaymentStatus.Pending)
        {
            Status = PaymentStatus.Declined;
            Reason = reason;
        }
    }

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
        string? driverId = null
    ) =>
        new()
        {
            Id = id,
            RiderId = riderId,
            DriverId = driverId,
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
