namespace SwiftRide.Payments.Domain;

public sealed class CheckoutAttempt
{
    public string Reference { get; init; } = Guid.NewGuid().ToString("N");
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ProviderTransaction { get; private set; }
    public string? PayDate { get; private set; }
    public string? BankCode { get; private set; }
    public bool RefundSubmitted { get; private set; }
    public DateTimeOffset? RefundRequestedAt { get; private set; }

    public void Confirm(string transaction, string payDate, string bankCode)
    {
        if (ProviderTransaction is not null && ProviderTransaction != transaction)
            throw new SwiftRide.SharedKernel.RuleException("Mã giao dịch provider không khớp.");
        ProviderTransaction = transaction; PayDate = payDate; BankCode = bankCode;
    }

    public void RequestRefund(DateTimeOffset now)
    {
        RefundSubmitted = true; RefundRequestedAt ??= now;
    }
}
