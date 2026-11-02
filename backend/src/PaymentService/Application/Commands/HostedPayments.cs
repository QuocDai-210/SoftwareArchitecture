using System.Text.Json;
using SwiftRide.Application.Serialization;
using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed record CheckoutView(Guid PaymentId, string Url);

public sealed record ProviderAck(string RspCode, string Message);

public sealed class HostedPayments(
    IPaymentReader reader,
    IPaymentWriter writer,
    ICheckoutStore checkouts,
    IWalletStore wallets,
    IUnitOfWork unit,
    IVnpayGateway provider,
    ICardProvider cards,
    IEventPublisher bus
)
{
    public async Task<PaymentView> TopUp(
        TopUpRequest request,
        string key,
        Actor actor,
        string correlation,
        CancellationToken ct
    )
    {
        if (!actor.IsRider)
            throw new RuleException("Cần quyền User.", ErrorKind.Forbidden);
        RequestKeys.Validate(key);
        if (
            request.Amount is < 10000 or > 5000000
            || request.Amount != decimal.Truncate(request.Amount)
        )
            throw new RuleException(
                "Nạp từ 10.000 đến 5.000.000 VND, số nguyên.",
                ErrorKind.Validation
            );
        var businessKey = "topup:" + RequestKeys.Hash(actor.Id + "|" + key);
        var hash = RequestKeys.Hash(
            JsonSerializer.Serialize(new { actor.Id, request.Amount }, ApplicationJson.Options)
        );
        await using var tx = await unit.Begin(ct);
        await writer.Lock(Guid.ParseExact(RequestKeys.Hash(businessKey)[..32], "N"), ct);
        var existing = await writer.ByKey(businessKey, ct);
        if (existing is not null)
        {
            RequestKeys.Same(existing.RequestHash, hash);
            return PaymentView.From(existing);
        }
        var id = Guid.NewGuid();
        var captured = await cards.Capture(
            new(id, actor.Id, request.Amount, PaymentMethod.Card, null, correlation),
            ct
        );
        if (captured.CheckoutReference is null)
            throw new RuleException("Nạp ví cần VNPAY sandbox.", ErrorKind.Unavailable);
        var payment = Payment.Captured(
            id,
            actor.Id,
            request.Amount,
            PaymentMethod.Card,
            hash,
            businessKey,
            correlation,
            false,
            null
        );
        payment.MarkTopUp();
        payment.AwaitCheckout(captured.CheckoutReference, 0);
        writer.Add(payment);
        await unit.Save(ct);
        await tx.Commit(ct);
        return PaymentView.From(payment);
    }

    public async Task<PaymentView> ByReference(string reference, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParseExact(reference, "N", out _))
            throw new RuleException("Mã checkout không hợp lệ.", ErrorKind.Validation);
        var attempt =
            await checkouts.Find(reference, ct)
            ?? throw new RuleException("Không có checkout.", ErrorKind.NotFound);
        return PaymentView.From(await Owned(attempt.PaymentId, actor, ct));
    }

    public async Task<CheckoutView> Checkout(Guid id, Actor actor, CancellationToken ct)
    {
        var payment = await Owned(id, actor, ct);
        if (payment.Status != PaymentStatus.Pending || payment.CheckoutReference is null)
            throw new RuleException("Giao dịch không chờ checkout.");
        var attempt =
            await checkouts.Find(payment.CheckoutReference, ct)
            ?? throw new RuleException("Không có checkout.", ErrorKind.NotFound);
        return new(id, provider.Checkout(attempt));
    }

    public async Task<ProviderAck> Notify(
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct
    )
    {
        ProviderReceipt receipt;
        try
        {
            receipt = provider.Verify(values);
        }
        catch (RuleException)
        {
            return new("97", "Invalid signature or payload");
        }
        var attempt = await checkouts.Find(receipt.Reference, ct);
        if (attempt is null)
            return new("01", "Order not found");
        if (receipt.Amount != attempt.Amount)
            return new("04", "Invalid amount");
        await Apply(attempt.PaymentId, receipt, ct);
        return new("00", "Confirm success");
    }

    public async Task<PaymentView> Reconcile(Guid id, Actor actor, CancellationToken ct)
    {
        var payment = await Owned(id, actor, ct);
        await Reconcile(id, ct);
        return PaymentView.From(await reader.Find(id, ct) ?? payment);
    }

    public async Task Reconcile(Guid id, CancellationToken ct)
    {
        var payment = await reader.Find(id, ct);
        if (
            payment?.CheckoutReference is null
            || payment.Status is not (PaymentStatus.Pending or PaymentStatus.RefundPending)
        )
            return;
        var attempt =
            await checkouts.Find(payment.CheckoutReference, ct)
            ?? throw new RuleException("Không có checkout.");
        if (payment.Status == PaymentStatus.Pending)
        {
            var receipt = await provider.Query(attempt, ct);
            if (receipt is not null)
                await Apply(id, receipt, ct);
            return;
        }
        bool submit;
        await using (var intent = await unit.Begin(ct))
        {
            await writer.Lock(id, ct);
            attempt =
                await checkouts.Find(payment.CheckoutReference, ct)
                ?? throw new RuleException("Không có checkout.");
            submit = !attempt.RefundSubmitted;
            if (submit)
                attempt.RequestRefund(DateTimeOffset.UtcNow);
            await unit.Save(ct);
            await intent.Commit(ct);
        }
        // ponytail: send refund once; uncertain outcomes stay pending for provider query and Ops review.
        var result = await provider.Refund(
            attempt,
            payment.Reason ?? "Hoàn tiền chuyến",
            submit,
            ct
        );
        if (!result.Confirmed)
            return;
        await using var tx = await unit.Begin(ct);
        await writer.Lock(id, ct);
        var tracked =
            await writer.FindTracked(id, ct) ?? throw new RuleException("Không có giao dịch.");
        if (tracked.Status != PaymentStatus.RefundPending)
            return;
        var charge = (await reader.Journals(id, ct)).Single(x => x.Kind == "Charge");
        writer.Add(
            new Journal
            {
                PaymentId = id,
                Kind = "Refund",
                Lines = Ledger.Reverse(charge.Lines),
            }
        );
        tracked.CompleteRefund();
        await bus.Publish(
            new PaymentRefunded(Guid.NewGuid(), id, id, tracked.Amount, tracked.CorrelationId),
            tracked.CorrelationId,
            ct
        );
        await unit.Save(ct);
        await tx.Commit(ct);
    }

    private async Task Apply(Guid id, ProviderReceipt receipt, CancellationToken ct)
    {
        await using var tx = await unit.Begin(ct);
        await writer.Lock(id, ct);
        var payment =
            await writer.FindTracked(id, ct)
            ?? throw new RuleException("Không có giao dịch.", ErrorKind.NotFound);
        if (payment.CheckoutReference != receipt.Reference)
            return;
        if (payment.Amount != receipt.Amount)
            throw new RuleException("Thông báo không khớp checkout.");
        if (payment.Status != PaymentStatus.Pending)
            return;
        var attempt =
            await checkouts.Find(receipt.Reference, ct)
            ?? throw new RuleException("Không có checkout.");
        if (receipt.Status == "00")
        {
            attempt.Confirm(receipt.Transaction, receipt.PayDate, receipt.BankCode);
            if (!payment.Settle())
                return;
            if (payment.IsTopUp)
            {
                var wallet = await wallets.GetOrCreateLocked(payment.RiderId, ct);
                wallet.Credit(payment.Amount);
                writer.Add(
                    new Journal
                    {
                        PaymentId = id,
                        Kind = "TopUp",
                        Lines =
                        [
                            new("ProviderReceivable", EntrySide.Debit, payment.Amount),
                            new(
                                "WalletLiability:" + payment.RiderId,
                                EntrySide.Credit,
                                payment.Amount
                            ),
                        ],
                    }
                );
            }
            else
            {
                writer.Add(
                    new Journal
                    {
                        PaymentId = id,
                        Kind = "Charge",
                        Lines = Ledger.Charge("ProviderReceivable", payment.Amount),
                    }
                );
                await bus.Publish(
                    new PaymentSettled(
                        Guid.NewGuid(),
                        id,
                        id,
                        payment.Amount,
                        "VND",
                        payment.CorrelationId
                    ),
                    payment.CorrelationId,
                    ct
                );
            }
        }
        else if (receipt.Status is "01" or "05" or "06")
            return;
        else
        {
            payment.Decline("VNPAY không hoàn tất giao dịch (" + receipt.Status + ").");
            if (!payment.IsTopUp)
                await bus.Publish(
                    new PaymentFailed(
                        Guid.NewGuid(),
                        id,
                        payment.Reason!,
                        false,
                        payment.CorrelationId
                    ),
                    payment.CorrelationId,
                    ct
                );
        }
        await unit.Save(ct);
        await tx.Commit(ct);
    }

    private async Task<Payment> Owned(Guid id, Actor actor, CancellationToken ct)
    {
        var payment =
            await reader.Find(id, ct)
            ?? throw new RuleException("Không có giao dịch.", ErrorKind.NotFound);
        if (!actor.IsOps && payment.RiderId != actor.Id)
            throw new RuleException("Giao dịch không thuộc tài khoản.", ErrorKind.Forbidden);
        return payment;
    }
}
