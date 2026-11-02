using SwiftRide.Contracts;
using SwiftRide.Payments.Application;
using SwiftRide.Payments.Application.Ports;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;

static class ApplicationChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var wallet = Wallet.Funded("rider", 100);
        var provider = new MemoryProvider();
        IPaymentMethod[] methods =
        [
            new CardMethod(provider),
            new WalletMethod(new MemoryWallet(wallet)),
        ];
        var factory = new PaymentMethodFactory(methods);
        foreach (var method in methods)
        {
            var request = new PaymentRequest(
                Guid.NewGuid(),
                "rider",
                40,
                method.Method,
                "demo-success",
                Guid.NewGuid().ToString()
            );
            check(
                ReferenceEquals(factory.Create(method.Method), method),
                "registry resolves supplied adapter " + method.Method
            );
            var captured = await method.Capture(request, default);
            check(captured.Success, "payment adapter accepts valid request " + method.Method);
            var lines = Ledger.Charge(method.DebitAccount("rider"), 40);
            Ledger.Validate(lines);
            check(
                lines[0].Account
                    == (
                        method.Method == PaymentMethod.Card
                            ? "ProviderReceivable"
                            : "WalletLiability:rider"
                    ),
                "funding account belongs to method " + method.Method
            );
            var payment = Payment.Captured(
                request.TripId,
                "rider",
                40,
                method.Method,
                "hash",
                "key",
                "correlation",
                true,
                null
            );
            await method.Refund(payment, default);
            payment.Refund("refund-key", "hash", "reason");
            check(
                payment.Status == PaymentStatus.Refunded,
                "refund updates domain state " + method.Method
            );
            try
            {
                payment.Compensate();
                throw new Exception("Refunded payment compensated twice");
            }
            catch (RuleException)
            {
                check(true, "refund blocks further compensation " + method.Method);
            }
        }
        check(
            provider.Captures == 1 && provider.Refunds == 1,
            "card delegates provider capture and refund"
        );
        check(wallet.Balance == 100, "wallet capture/refund restores balance");
        check(
            !wallet.TryDebit(101) && wallet.Balance == 100,
            "insufficient funds leave balance intact"
        );
        try
        {
            wallet.Credit(-1);
            throw new Exception("Negative wallet credit accepted");
        }
        catch (RuleException)
        {
            check(wallet.Balance == 100, "wallet rejects negative credit without mutation");
        }
        var replacement = new ReplacementMethod();
        check(
            ReferenceEquals(
                new PaymentMethodFactory([replacement]).Create(PaymentMethod.Card),
                replacement
            ),
            "replace payment adapter without editing factory"
        );
        try
        {
            factory.Create((PaymentMethod)42);
            throw new Exception("Unknown payment method accepted");
        }
        catch (RuleException ex)
        {
            check(ex.Kind == ErrorKind.Validation, "unknown payment method rejected");
        }
    }

    private sealed class MemoryWallet(Wallet wallet) : IWalletStore
    {
        public Task<Wallet> GetOrCreateLocked(string riderId, CancellationToken ct) =>
            Task.FromResult(wallet);

        public Task<Wallet?> GetLocked(string riderId, CancellationToken ct) =>
            Task.FromResult<Wallet?>(wallet.RiderId == riderId ? wallet : null);
    }

    private sealed class MemoryProvider : ICardProvider
    {
        public int Captures;
        public int Refunds;

        public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
        {
            Captures++;
            return Task.FromResult(new CaptureResult(true));
        }

        public Task Refund(Payment payment, CancellationToken ct)
        {
            Refunds++;
            return Task.CompletedTask;
        }
    }

    private sealed class ReplacementMethod : IPaymentMethod
    {
        public PaymentMethod Method => PaymentMethod.Card;

        public string DebitAccount(string riderId) => "ReplacementClearing";

        public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) =>
            Task.FromResult(new CaptureResult(false, "Declined"));

        public Task Refund(Payment payment, CancellationToken ct) => Task.CompletedTask;
    }
}
