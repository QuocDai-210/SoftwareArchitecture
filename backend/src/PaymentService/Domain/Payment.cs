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

    public void AwaitCheckout(string reference, long attempt)
    {
        if (Status is not (PaymentStatus.Declined or PaymentStatus.Pending))
            throw new RuleException("Không thể tạo thanh toán mới.");
        Status = PaymentStatus.Pending; CheckoutReference = reference; Attempt = attempt; Reason = null;
    }

    public void MarkTopUp() => IsTopUp = true;

    public bool Settle()
    {
        if (Status == PaymentStatus.Settled) return false;
        if (Status != PaymentStatus.Pending) throw new RuleException("Giao dịch không chờ thanh toán.");
        Status = PaymentStatus.Settled; Reason = null; return true;
    }

    public void Decline(string reason)
    {
        if (Status == PaymentStatus.Pending) { Status = PaymentStatus.Declined; Reason = reason; }
    }

    public void RequestRefund(string key, string hash, string reason)
    {
        EnsureSettled(); Status = PaymentStatus.RefundPending; RefundKey = key; RefundHash = hash; Reason = reason;
    }

    public void CompleteRefund()
    {
        if (Status != PaymentStatus.RefundPending) throw new RuleException("Giao dịch không chờ hoàn tiền.");
        Status = PaymentStatus.Refunded;
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
        string? reason
    ) =>
        new()
        {
            Id = id,
            RiderId = riderId,
            Amount = amount,
            Method = method,
            RequestHash = hash,
            IdempotencyKey = key,
            CorrelationId = correlation,
            Status = success ? PaymentStatus.Settled : PaymentStatus.Declined,
            Reason = reason,
        };

    public void Compensate()
    {
        EnsureSettled();
        Status = PaymentStatus.Compensated;
        Reason = "Saga demo thất bại sau thu tiền; khoản thu đã được hoàn.";
    }

    public void Refund(string key, string hash, string reason)
    {
        EnsureSettled();
        Status = PaymentStatus.Refunded;
        RefundKey = key;
        RefundHash = hash;
        Reason = reason;
    }

    private void EnsureSettled()
    {
        if (Status != PaymentStatus.Settled)
            throw new RuleException("Chỉ hoàn khoản đã thanh toán.");
    }
}
