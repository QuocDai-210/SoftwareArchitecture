namespace SwiftRide.Payments.Application.Wallets;

public sealed class WalletFunding(IWalletAccounts accounts, IUnitOfWork unit, TimeProvider clock)
    : IWalletFunding
{
    public async Task Credit(SandboxPayment payment, CancellationToken ct)
    {
        if (payment.Purpose != "WalletTopUp" || payment.State != "Succeeded")
        {
            throw new RuleException("Chỉ cộng ví cho giao dịch nạp đã được xác minh.");
        }
        // Joins the caller's transaction: receipt, wallet and journal commit together.
        await using var tx = await unit.Begin(ct);
        await accounts.Lock(payment.OwnerId, ct);
        var previous = await accounts.FindActivity(payment.Reference, ct);
        if (previous is not null)
        {
            if (previous.OwnerId != payment.OwnerId || previous.Amount != payment.Amount)
            {
                throw new RuleException("Giao dịch nạp đã được ghi nhận với dữ liệu khác.");
            }
            return;
        }
        var wallet = await accounts.Find(payment.OwnerId, ct);
        if (wallet is null)
        {
            wallet = new WalletAccount { OwnerId = payment.OwnerId };
            accounts.Add(wallet);
        }
        wallet.CreditTopUp(payment.Amount);
        var now = clock.GetUtcNow();
        var journal = new Journal
        {
            PaymentId = Guid.ParseExact(payment.Reference, "N"),
            Kind = "WalletTopUp",
            CreatedAt = now,
            Lines =
            [
                new("VnpaySandboxClearing", EntrySide.Debit, payment.Amount),
                new($"Wallet:{payment.OwnerId}:Available", EntrySide.Credit, payment.Amount),
            ],
        };
        Ledger.Validate(journal.Lines);
        accounts.Add(journal);
        accounts.Add(
            new WalletActivity
            {
                Reference = payment.Reference,
                OwnerId = payment.OwnerId,
                JournalId = journal.Id,
                Amount = payment.Amount,
                AvailableAfter = wallet.Available,
                HeldAfter = wallet.Held,
                PendingAfter = wallet.PendingEarnings,
                CreatedAt = now,
            }
        );
        await unit.Save(ct);
        await tx.Commit(ct);
    }
}
