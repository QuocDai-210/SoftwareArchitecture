using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class WalletMethod(IWalletStore wallets) : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Wallet;

    public string DebitAccount(string riderId) => $"WalletLiability:{riderId}";

    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        var wallet = await wallets.GetLocked(request.RiderId, ct);
        if (wallet is null || !wallet.TryDebit(request.Amount))
            return new(false, "Ví không đủ tiền.");
        return new(true);
    }

    public async Task Refund(Payment payment, CancellationToken ct)
    {
        var wallet =
            await wallets.GetLocked(payment.RiderId, ct)
            ?? throw new RuleException("Không tìm thấy ví.", ErrorKind.NotFound);
        wallet.Credit(payment.Amount);
    }
}
