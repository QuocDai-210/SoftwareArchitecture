namespace SwiftRide.Payments.Application.Wallets;

public sealed class WalletWithdrawals(
    IWalletAccounts accounts,
    IWalletWithdrawalStore withdrawals,
    IUnitOfWork unit,
    TimeProvider clock
)
{
    public async Task<WalletWithdrawalView> Request(
        WalletWithdrawalRequest request,
        string key,
        Actor actor,
        CancellationToken ct
    )
    {
        RequireWalletOwner(actor);
        RequestKeys.Validate(key);
        var normalized = Normalize(request);
        var hash = RequestKeys.Hash(normalized.Amount.ToString());
        await using var tx = await unit.Begin(ct);
        await accounts.Lock(actor.Id, ct);
        var existing = await withdrawals.FindByKey(actor.Id, key, ct);
        if (existing is not null)
        {
            RequestKeys.Same(existing.RequestHash, hash);
            return WalletWithdrawalView.From(existing);
        }
        var wallet = await accounts.Find(actor.Id, ct);
        if (wallet is null)
            throw new RuleException("Ví chưa có số dư khả dụng.", ErrorKind.Validation);
        wallet.HoldWithdrawal(normalized.Amount);
        var withdrawal = new WalletWithdrawal
        {
            OwnerId = actor.Id,
            RequestKey = key,
            RequestHash = hash,
            Amount = normalized.Amount,
            RequestedAt = clock.GetUtcNow(),
        };
        withdrawals.Add(withdrawal);
        AddActivity(
            withdrawal,
            wallet,
            "WalletWithdrawalRequested",
            new($"Wallet:{actor.Id}:Available", EntrySide.Debit, normalized.Amount),
            new($"Wallet:{actor.Id}:Held", EntrySide.Credit, normalized.Amount)
        );
        await unit.Save(ct);
        await tx.Commit(ct);
        return WalletWithdrawalView.From(withdrawal);
    }

    public async Task<PageResult<WalletWithdrawalView>> History(
        int page,
        Actor actor,
        CancellationToken ct
    )
    {
        RequireWalletOwner(actor);
        ValidatePage(page);
        var result = await withdrawals.History(actor.Id, page, ct);
        return new(
            result.Items.Select(WalletWithdrawalView.From).ToArray(),
            page,
            10,
            result.Total
        );
    }

    public async Task<PageResult<WalletWithdrawalView>> Pending(
        int page,
        Actor actor,
        CancellationToken ct
    )
    {
        RequireOps(actor);
        ValidatePage(page);
        var result = await withdrawals.Pending(page, ct);
        return new(
            result.Items.Select(WalletWithdrawalView.From).ToArray(),
            page,
            10,
            result.Total
        );
    }

    public async Task<WalletWithdrawalView> Review(
        Guid id,
        WalletWithdrawalDecision decision,
        Actor actor,
        CancellationToken ct
    )
    {
        RequireOps(actor);
        var reason = decision.Reason?.Trim() ?? "";
        if (reason.Length > 200 || (!decision.Approve && reason.Length == 0))
            throw new RuleException(
                "Khi từ chối cần nhập lý do tối đa 200 ký tự.",
                ErrorKind.Validation
            );
        await using var tx = await unit.Begin(ct);
        var withdrawal = await withdrawals.Find(id, ct)
            ?? throw new RuleException("Không tìm thấy yêu cầu rút tiền.", ErrorKind.NotFound);
        await accounts.Lock(withdrawal.OwnerId, ct);
        withdrawal = await withdrawals.Find(id, ct)
            ?? throw new RuleException("Không tìm thấy yêu cầu rút tiền.", ErrorKind.NotFound);
        var target = decision.Approve ? "Completed" : "Rejected";
        if (withdrawal.State == target)
        {
            await tx.Commit(ct);
            return WalletWithdrawalView.From(withdrawal);
        }
        if (withdrawal.State != "Pending")
            throw new RuleException("Yêu cầu rút tiền đã được xử lý theo quyết định khác.");
        var wallet = await accounts.Find(withdrawal.OwnerId, ct)
            ?? throw new RuleException("Không tìm thấy ví.", ErrorKind.NotFound);
        if (decision.Approve)
        {
            wallet.CompleteWithdrawal(withdrawal.Amount);
            withdrawal.Complete(actor.Id, clock.GetUtcNow());
            AddActivity(
                withdrawal,
                wallet,
                "WalletWithdrawalPaid",
                new($"Wallet:{withdrawal.OwnerId}:Held", EntrySide.Debit, withdrawal.Amount),
                new("SimulatedWithdrawalClearing", EntrySide.Credit, withdrawal.Amount)
            );
        }
        else
        {
            wallet.Release(withdrawal.Amount);
            withdrawal.Reject(actor.Id, reason, clock.GetUtcNow());
            AddActivity(
                withdrawal,
                wallet,
                "WalletWithdrawalRejected",
                new($"Wallet:{withdrawal.OwnerId}:Held", EntrySide.Debit, withdrawal.Amount),
                new($"Wallet:{withdrawal.OwnerId}:Available", EntrySide.Credit, withdrawal.Amount)
            );
        }
        await unit.Save(ct);
        await tx.Commit(ct);
        return WalletWithdrawalView.From(withdrawal);
    }

    private void AddActivity(
        WalletWithdrawal withdrawal,
        WalletAccount wallet,
        string kind,
        params LedgerLine[] lines
    )
    {
        var journal = new Journal { PaymentId = withdrawal.Id, Kind = kind, Lines = [.. lines] };
        Ledger.Validate(journal.Lines);
        accounts.Add(journal);
        accounts.Add(
            new WalletActivity
            {
                Reference = $"withdraw:{withdrawal.Id:N}:{kind}",
                OwnerId = withdrawal.OwnerId,
                JournalId = journal.Id,
                Kind = kind,
                Amount = withdrawal.Amount,
                AvailableAfter = wallet.Available,
                HeldAfter = wallet.Held,
                PendingAfter = wallet.PendingEarnings,
                CreatedAt = clock.GetUtcNow(),
            }
        );
    }

    private static WalletWithdrawalRequest Normalize(WalletWithdrawalRequest request)
    {
        if (request.Amount is < 10000 or > 5000000)
            throw new RuleException("Thông tin rút tiền không hợp lệ.", ErrorKind.Validation);
        return request;
    }

    private static void RequireWalletOwner(Actor actor)
    {
        if ((!actor.IsRider && !actor.IsDriver) || actor.IsOps || actor.IsService)
            throw new RuleException("Ví dành cho tài khoản User hoặc Driver.", ErrorKind.Forbidden);
    }

    private static void RequireOps(Actor actor)
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
    }

    private static void ValidatePage(int page)
    {
        if (page is < 1 or > 100000)
            throw new RuleException("Trang không hợp lệ.", ErrorKind.Validation);
    }
}

public sealed record WalletWithdrawalRequest(
    long Amount,
    string BankName,
    string AccountNumber,
    string AccountHolder
);

public sealed record WalletWithdrawalDecision(bool Approve, string? Reason = null);

public sealed record WalletWithdrawalView(
    Guid Id,
    string OwnerId,
    long Amount,
    string State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? DecisionReason
)
{
    public static WalletWithdrawalView From(WalletWithdrawal withdrawal) => new(
        withdrawal.Id,
        withdrawal.OwnerId,
        withdrawal.Amount,
        withdrawal.State,
        withdrawal.RequestedAt,
        withdrawal.ReviewedAt,
        withdrawal.ReviewedBy,
        withdrawal.DecisionReason
    );
}
