namespace SwiftRide.Payments.Application.Ports;

public sealed record ProviderReceipt(string Reference, decimal Amount, string Transaction, string PayDate,
    string BankCode, string Status);
public sealed record ProviderRefund(bool Confirmed, string? Reason = null);

public interface IVnpayGateway
{
    string Checkout(CheckoutAttempt attempt);
    ProviderReceipt Verify(IReadOnlyDictionary<string, string> values);
    Task<ProviderReceipt?> Query(CheckoutAttempt attempt, CancellationToken ct);
    Task<ProviderRefund> Refund(CheckoutAttempt attempt, string reason, bool submit, CancellationToken ct);
}
