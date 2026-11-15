using Moq;
using SwiftRide.Contracts;
using SwiftRide.Payments.Application;
using SwiftRide.Payments.Application.Wallets;
using SwiftRide.Payments.Domain;
using SwiftRide.SharedKernel;

static class ApplicationChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var accounts = new Mock<IWalletAccounts>();
        var settled = false;
        accounts
            .Setup(x => x.SettleTrip(It.IsAny<Guid>(), "rider", "driver", 40, default, null))
            .Callback(() => settled = true)
            .Returns(Task.CompletedTask);
        var walletMethod = new WalletMethod(accounts.Object);
        var request = new PaymentRequest(
            Guid.NewGuid(),
            "rider",
            40,
            PaymentMethod.Wallet,
            null,
            Guid.NewGuid().ToString(),
            DriverId: "driver"
        );
        var result = await walletMethod.Capture(request, default);
        check(result.Success && settled, "wallet payment settles held trip funds once");
        check(
            walletMethod.DebitAccount("rider") == "Wallet:rider:Held",
            "wallet payment debits the held wallet account"
        );

        var insufficient = new WalletAccount { OwnerId = "rider" };
        try
        {
            insufficient.Hold(100);
            throw new Exception("Insufficient wallet balance accepted");
        }
        catch (RuleException)
        {
            check(
                insufficient.Available == 0 && insufficient.Held == 0,
                "insufficient balance leaves wallet unchanged"
            );
        }
        insufficient.CreditTopUp(10000);
        insufficient.Hold(7000);
        check(
            insufficient.Available == 3000 && insufficient.Held == 7000,
            "wallet hold transfers available balance to held"
        );
        insufficient.Release(7000);
        check(
            insufficient.Available == 10000 && insufficient.Held == 0,
            "wallet release restores available balance"
        );

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
            cashResult.Outcome == CaptureOutcome.Pending,
            "COD waits for the assigned driver to collect cash"
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
            "dropoff never settles COD before collection"
        );
        check(
            cod.CollectCash("cash-key", "cash-hash", "driver", null),
            "first cash collection changes payment state"
        );
        check(
            cod.Status == PaymentStatus.Settled && cod.CollectedBy == "driver",
            "cash collection records its assigned driver"
        );
        check(
            !cod.CollectCash("cash-key", "cash-hash", "driver", null),
            "cash collection replay is idempotent"
        );

        var cashLines = Ledger.Charge(cash.DebitAccount("driver"), cod.Amount);
        Ledger.Validate(cashLines);
        Ledger.Validate(Ledger.Reverse(cashLines));
        cod.Refund("refund-key", "refund-hash", "Cash returned", "driver");
        check(
            cod.Status == PaymentStatus.Refunded && cod.RefundedBy == "driver",
            "COD refund records assigned driver and refund state"
        );
        try
        {
            new PaymentMethodFactory([walletMethod, cash]).Create((PaymentMethod)42);
            throw new Exception("Unknown payment method accepted");
        }
        catch (RuleException ex)
        {
            check(ex.Kind == ErrorKind.Validation, "unknown payment method is rejected");
        }
    }
}
