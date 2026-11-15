// Persistence-only shapes for existing migration history. No active provider workflow.
namespace SwiftRide.Payments.Domain
{
    // Historical persistence shape retained for existing migrations and audit data.
    // No active checkout, provider callback, or wallet funding workflow uses this entity.
    public sealed class SandboxPayment
    {
        public string Reference { get; set; } = Guid.NewGuid().ToString("N");
        public string OwnerId { get; set; } = "";
        public string RequestKey { get; set; } = "";
        public long Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string State { get; private set; } = "Pending";
        public string? ProviderTransaction { get; private set; }
        public DateTimeOffset? ConfirmedAt { get; private set; }
        public DateTimeOffset NextCheckAt { get; set; }
        public int CheckCount { get; set; }
        public DateTimeOffset? NextQueryAt { get; set; }
        public DateTimeOffset? LastIpnAt { get; set; }
        public string Purpose { get; init; } = "Sandbox";
        public DateTimeOffset? WalletCreditedAt { get; set; }
    }
}

namespace SwiftRide.Payments.Domain
{
    // Historical provider metadata stays readable without an active checkout workflow.
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
    }
}

namespace SwiftRide.Payments.Domain
{
    // Retained only to map balances from deployments preceding WalletAccount.
    public sealed class Wallet
    {
        public string RiderId { get; init; } = "";
        public decimal Balance { get; private set; }
    }
}

namespace SwiftRide.Payments.Infrastructure
{
    public sealed class ProviderOperation
    {
        public string Key { get; set; } = "";
        public decimal Amount { get; set; }
        public string RequestHash { get; set; } = "";
        public string State { get; set; } = "new";
        public int Attempts { get; set; }
        public bool UnknownInjected { get; set; }
        public bool Recovered { get; set; }
    }
}
