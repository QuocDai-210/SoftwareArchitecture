using SwiftRide.Contracts;
using SwiftRide.Payments.Application;
using SwiftRide.Payments.Application.Ports;
using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;
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
            new WalletMethod(new MemoryWalletAccounts(wallet)),
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
                Guid.NewGuid().ToString(),
                DriverId: "driver"
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
                            : "Wallet:rider:Held"
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
            if (method.Method == PaymentMethod.Wallet)
            {
                try
                {
                    await method.Refund(payment, default);
                    throw new Exception("Wallet refund bypassed the approved refund flow");
                }
                catch (RuleException)
                {
                    check(true, "wallet refund requires an approved trip refund entry");
                }
            }
            else
            {
                await method.Refund(payment, default);
            }
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
        var cash = new CashMethod();
        var cashRequest = new PaymentRequest(
            Guid.NewGuid(),
            "rider",
            40000,
            PaymentMethod.Cod,
            null,
            "cash-correlation",
            DriverId: "driver"
        );
        var cashResult = await cash.Capture(cashRequest, default);
        check(
            cashResult.Outcome == CaptureOutcome.Pending && !cashResult.Success,
            "COD capture waits for physical cash"
        );
        var cod = Payment.Captured(
            cashRequest.TripId,
            "rider",
            40000,
            PaymentMethod.Cod,
            "hash",
            "charge-key",
            "correlation",
            false,
            null
        );
        cod.AwaitCash("driver");
        check(
            cod.Status == PaymentStatus.Pending && cod.CollectedAt is null,
            "dropoff never settles COD"
        );
        check(
            cod.CollectCash("cash-key", "cash-hash", "driver", null),
            "first cash collection changes state"
        );
        var collectedAt = cod.CollectedAt;
        check(
            cod.Status == PaymentStatus.Settled
                && cod.CollectedBy == "driver"
                && collectedAt is not null,
            "cash collection keeps actor and timestamp"
        );
        check(
            !cod.CollectCash("cash-key", "cash-hash", "driver", null)
                && cod.CollectedAt == collectedAt,
            "cash replay leaves state and timestamp unchanged"
        );
        foreach (
            var (key, hash) in new[] { ("other-key", "cash-hash"), ("cash-key", "other-hash") }
        )
        {
            try
            {
                cod.CollectCash(key, hash, "driver", null);
                throw new Exception("Conflicting cash collection accepted");
            }
            catch (RuleException)
            {
                check(true, "cash idempotency rejects key or payload conflict");
            }
        }
        try
        {
            cod.AwaitCash("driver");
            throw new Exception("Settled cash reopened");
        }
        catch (RuleException)
        {
            check(true, "settled cash cannot return to pending");
        }
        var cashLines = Ledger.Charge(cash.DebitAccount("driver"), cod.Amount);
        check(
            cashLines[0].Account == "CashHeldByDriver:driver",
            "cash is held by assigned driver, never rider wallet"
        );
        Ledger.Validate(cashLines);
        cod.Refund("refund-cash-key", "refund-hash", "Returned to rider", "ops");
        check(
            cod.RefundedBy == "ops"
                && cod.RefundedAt is not null
                && cod.Status == PaymentStatus.Refunded,
            "cash refund records operator and time"
        );
        check(
            !cod.CollectCash("cash-key", "cash-hash", "driver", null)
                && cod.Status == PaymentStatus.Refunded,
            "late collection replay cannot reopen refunded cash"
        );
        Ledger.Validate(Ledger.Reverse(cashLines));
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

    private sealed class MemoryWalletAccounts(Wallet wallet) : IWalletAccounts
    {
        private readonly WalletAccount _account = new() { OwnerId = wallet.RiderId };

        public Task Lock(string owner, CancellationToken ct) => Task.CompletedTask;

        public Task<WalletAccount?> Find(string owner, CancellationToken ct) =>
            Task.FromResult<WalletAccount?>(_account.OwnerId == owner ? _account : null);

        public Task<WalletActivity?> FindActivity(string reference, CancellationToken ct) =>
            Task.FromResult<WalletActivity?>(null);

        public Task<PageResult<WalletActivity>> History(
            string owner,
            int page,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<PageResult<SandboxView>> TopUps(string owner, int page, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task HoldTrip(Guid tripId, string riderId, long amount, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ReleaseTrip(Guid tripId, string riderId, long amount, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SettleTrip(
            Guid tripId,
            string riderId,
            string driverId,
            long amount,
            CancellationToken ct
        ) => Task.CompletedTask;

        public void Add(WalletAccount account) { }

        public void Add(WalletActivity activity) { }

        public void Add(Journal journal) { }
    }

    private sealed class MemoryProvider : ICardProvider
    {
        public int Captures;
        public int Refunds;

        public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
        {
            Captures++;
            return Task.FromResult(new CaptureResult(CaptureOutcome.Settled));
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
            Task.FromResult(new CaptureResult(CaptureOutcome.Declined, "Declined"));

        public Task Refund(Payment payment, CancellationToken ct) => Task.CompletedTask;
    }
}
