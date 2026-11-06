using SwiftRide.Payments.Domain;

namespace SwiftRide.Payments.Application.Sandbox;

public interface ISandboxStore
{
    Task Lock(string key, CancellationToken ct);
    Task<SandboxPayment?> Find(string reference, CancellationToken ct);
    Task<SandboxPayment?> FindRequest(string owner, string key, CancellationToken ct);
    Task<IReadOnlyList<string>> Due(DateTimeOffset now, CancellationToken ct);
    void Add(SandboxPayment payment);
}

public sealed record SandboxReceipt(
    string Reference,
    long Amount,
    string Transaction,
    string Status,
    string Response
);

public interface ISandboxVnpay
{
    string Checkout(SandboxPayment payment);
    SandboxReceipt? Verify(IReadOnlyDictionary<string, string> fields);
    Task<SandboxReceipt?> Query(SandboxPayment payment, CancellationToken ct);
}

public sealed class SandboxQueryDeferredException : Exception;

public sealed record SandboxView(
    string Reference,
    long Amount,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    string? CheckoutUrl = null,
    DateTimeOffset? NextQueryAt = null,
    string Purpose = "Sandbox",
    DateTimeOffset? WalletCreditedAt = null
);
