using System.Security.Cryptography;
using System.Text;

namespace SwiftRide.Payments.Application.Wallets;

public sealed class WalletFunding(IWalletAccounts accounts, IUnitOfWork unit, TimeProvider clock)
    : IWalletFunding
{
    public async Task<WalletActivity> TopUp(
        long amount,
        string key,
        Actor actor,
        CancellationToken ct
    )
    {
        if (!actor.IsRider || actor.IsDriver || actor.IsService)
            throw new RuleException("Only Riders can top up a wallet.", ErrorKind.Forbidden);
        if (amount is < 10000 or > 5000000)
            throw new RuleException(
                "Top-up amount must be from 10,000 to 5,000,000 VND.",
                ErrorKind.Validation
            );
        RequestKeys.Validate(key);
        var reference =
            "topup:"
            + Convert
                .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor.Id}:{key}")))
                .ToLowerInvariant();
        await using var tx = await unit.Begin(ct);
        await accounts.Lock(actor.Id, ct);
        var existing = await accounts.FindActivity(reference, ct);
        if (existing is not null)
        {
            if (
                existing.OwnerId != actor.Id
                || existing.Amount != amount
                || existing.Kind != "WalletTopUp"
            )
                throw new RuleException("Idempotency-Key was already used with another request.");
            await tx.Commit(ct);
            return existing;
        }
        var wallet = await accounts.Find(actor.Id, ct);
        if (wallet is null)
        {
            wallet = new WalletAccount { OwnerId = actor.Id };
            accounts.Add(wallet);
        }
        wallet.CreditTopUp(amount);
        var now = clock.GetUtcNow();
        var journal = new Journal
        {
            PaymentId = new Guid(Convert.FromHexString(reference[6..38])),
            Kind = "SimulatedWalletTopUp",
            CreatedAt = now,
            Lines =
            [
                new("SimulatedTopUpClearing", EntrySide.Debit, amount),
                new($"Wallet:{actor.Id}:Available", EntrySide.Credit, amount),
            ],
        };
        Ledger.Validate(journal.Lines);
        accounts.Add(journal);
        var activity = new WalletActivity
        {
            Reference = reference,
            OwnerId = actor.Id,
            JournalId = journal.Id,
            Kind = "WalletTopUp",
            Amount = amount,
            AvailableAfter = wallet.Available,
            HeldAfter = wallet.Held,
            PendingAfter = wallet.PendingEarnings,
            CreatedAt = now,
        };
        accounts.Add(activity);
        await unit.Save(ct);
        await tx.Commit(ct);
        return activity;
    }
}

public sealed record WalletTopUpRequest(long Amount);
