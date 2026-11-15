using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Application;

public sealed class WalletMethod(IWalletAccounts accounts) : IPaymentMethod
{
    public PaymentMethod Method => PaymentMethod.Wallet;

    public string DebitAccount(string riderId) => $"Wallet:{riderId}:Held";

    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        if (
            request.Amount is < 0 or > 100000000
            || request.Amount != decimal.Round(request.Amount, 0)
            || string.IsNullOrWhiteSpace(request.DriverId)
        )
            return new(CaptureOutcome.Declined, "Yêu cầu thanh toán ví không hợp lệ.");
        try
        {
            await accounts.SettleTrip(
                request.TripId,
                request.RiderId,
                request.DriverId,
                decimal.ToInt64(request.Amount),
                ct,
                request.WalletHoldId
            );
        }
        catch (RuleException ex)
        {
            return new(CaptureOutcome.Declined, ex.Message);
        }
        return new(CaptureOutcome.Settled);
    }

    public Task Refund(Payment payment, CancellationToken ct) =>
        throw new RuleException("Hoàn chuyến ví không thuộc phạm vi mô phỏng.");
}
