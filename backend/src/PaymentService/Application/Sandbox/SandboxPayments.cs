using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Application.Sandbox;

public sealed class SandboxPayments(
    ISandboxStore store,
    ISandboxVnpay provider,
    IUnitOfWork unit,
    TimeProvider clock,
    IWalletFunding walletFunding
)
{
    public Task<SandboxView> Create(long amount, string key, Actor actor, CancellationToken ct) =>
        Create(amount, key, actor, "Sandbox", ct);

    public Task<SandboxView> CreateTopUp(
        long amount,
        string key,
        Actor actor,
        CancellationToken ct
    ) => Create(amount, key, actor, "WalletTopUp", ct);

    private async Task<SandboxView> Create(
        long amount,
        string key,
        Actor actor,
        string purpose,
        CancellationToken ct
    )
    {
        if (!actor.IsRider || actor.IsDriver || actor.IsOps || actor.IsService)
        {
            throw new RuleException("Cần tài khoản User để thử thanh toán.", ErrorKind.Forbidden);
        }
        RequestKeys.Validate(key);
        if (amount < 10000 || amount > 5000000)
        {
            throw new RuleException("Số tiền từ 10.000 đến 5.000.000 đồng.", ErrorKind.Validation);
        }
        await using var tx = await unit.Begin(ct);
        await store.Lock($"sandbox-create:{actor.Id}:{key}", ct);
        var payment = await store.FindRequest(actor.Id, key, ct);
        if (payment is null)
        {
            var now = clock.GetUtcNow();
            payment = new SandboxPayment
            {
                OwnerId = actor.Id,
                RequestKey = key,
                Amount = amount,
                Purpose = purpose,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(15),
                NextCheckAt = now.AddMinutes(2),
            };
            store.Add(payment);
        }
        else if (payment.Amount != amount || payment.Purpose != purpose)
        {
            throw new RuleException("Mã yêu cầu đã được dùng với số tiền khác.");
        }
        var url =
            payment.State == "Pending" && payment.ExpiresAt > clock.GetUtcNow()
                ? provider.Checkout(payment)
                : null;
        await unit.Save(ct);
        await tx.Commit(ct);
        return View(payment) with { CheckoutUrl = url };
    }

    public async Task<SandboxView> Get(string reference, Actor actor, CancellationToken ct)
    {
        var payment = await Owned(reference, actor, ct);
        return View(payment);
    }

    public async Task<SandboxView> Reconcile(string reference, Actor actor, CancellationToken ct)
    {
        await Owned(reference, actor, ct);
        await Reconcile(reference, true, ct);
        return await Get(reference, actor, ct);
    }

    public async Task<string> Notify(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken ct
    )
    {
        var receipt = provider.Verify(fields);
        return receipt is null ? "97" : await Apply(receipt, true, ct);
    }

    public Task Reconcile(string reference, CancellationToken ct) =>
        Reconcile(reference, false, ct);

    private async Task Reconcile(string reference, bool interactive, CancellationToken ct)
    {
        SandboxPayment? payment;
        await using (var tx = await unit.Begin(ct))
        {
            await store.Lock($"sandbox:{reference}", ct);
            payment = await store.Find(reference, ct);
            var now = clock.GetUtcNow();
            if (
                payment is null
                || payment.State is "Succeeded" or "Failed"
                || payment.NextQueryAt > now
                || (!interactive && (payment.NextCheckAt > now || payment.CheckCount >= 12))
            )
            {
                return;
            }
            payment.NextQueryAt = now.AddSeconds(15);
            if (!interactive)
            {
                payment.CheckCount++;
                payment.NextCheckAt = now.AddMinutes(Math.Min(60, payment.CheckCount * 2));
            }
            await unit.Save(ct);
            await tx.Commit(ct);
        }
        // No database transaction is held while contacting the provider.
        try
        {
            var receipt = await provider.Query(payment, ct);
            if (receipt is not null && receipt.Reference == reference)
            {
                await Apply(receipt, false, ct);
            }
        }
        catch (SandboxQueryDeferredException)
        {
            await using var tx = await unit.Begin(ct);
            await store.Lock($"sandbox:{reference}", ct);
            var current = await store.Find(reference, ct);
            if (current is not null)
            {
                current.NextQueryAt = clock.GetUtcNow().AddMinutes(5);
                await unit.Save(ct);
            }
            await tx.Commit(ct);
        }
    }

    private async Task<string> Apply(SandboxReceipt receipt, bool fromIpn, CancellationToken ct)
    {
        await using var tx = await unit.Begin(ct);
        await store.Lock($"sandbox:{receipt.Reference}", ct);
        var payment = await store.Find(receipt.Reference, ct);
        if (payment is null)
        {
            return "01";
        }
        if (payment.Amount != receipt.Amount)
        {
            return "04";
        }
        if (fromIpn)
        {
            payment.LastIpnAt = clock.GetUtcNow();
        }
        var changed = payment.Record(
            receipt.Status,
            receipt.Response,
            receipt.Transaction,
            clock.GetUtcNow()
        );
        if (
            payment.Purpose == "WalletTopUp"
            && payment.State == "Succeeded"
            && payment.WalletCreditedAt is null
        )
        {
            await walletFunding.Credit(payment, ct);
            payment.WalletCreditedAt = clock.GetUtcNow();
        }
        await unit.Save(ct);
        await tx.Commit(ct);
        return changed ? "00" : "02";
    }

    private async Task<SandboxPayment> Owned(string reference, Actor actor, CancellationToken ct)
    {
        var payment = await store.Find(reference, ct);
        if (payment is null || (payment.OwnerId != actor.Id && !actor.IsOps))
        {
            throw new RuleException("Không tìm thấy giao dịch.", ErrorKind.NotFound);
        }
        return payment;
    }

    private static SandboxView View(SandboxPayment p) =>
        new(
            p.Reference,
            p.Amount,
            p.State,
            p.CreatedAt,
            p.ExpiresAt,
            p.ConfirmedAt,
            NextQueryAt: p.NextQueryAt,
            Purpose: p.Purpose,
            WalletCreditedAt: p.WalletCreditedAt
        );
}
