using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class WalletMethod(IWalletAccounts accounts) : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Wallet;

    public string DebitAccount(string riderId) => $"Wallet:{riderId}";

    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
        await accounts.Debit(request.RiderId, checked((long)request.Amount), ct)
            ? new(CaptureOutcome.Settled)
            : new(CaptureOutcome.Declined, "Insufficient wallet balance.");

    public Task Refund(Payment payment, CancellationToken ct) =>
        accounts.Credit(payment.RiderId, checked((long)payment.Amount), ct);
}
