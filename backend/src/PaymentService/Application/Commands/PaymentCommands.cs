using System.Text.Json;
using SwiftRide.Application.Serialization;
using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class PaymentCommands(
    IPaymentWriter writer,
    IPaymentReader reader,
    IUnitOfWork unit,
    PaymentMethodFactory factory,
    IEventPublisher bus,
    IPaymentMetrics metrics
)
{
    public async Task<Payment> Charge(PaymentRequest request, string key, CancellationToken ct)
    {
        RequestKeys.Validate(key);
        if (
            request.TripId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.RiderId)
            || request.RiderId.Length > 100
            || request.Amount is < 0 or > 100000000
            || request.Amount != decimal.Round(request.Amount, 0)
        )
            throw new RuleException("Yêu cầu thanh toán không hợp lệ.", ErrorKind.Validation);
        if (key != $"trip:{request.TripId}:charge")
            throw new RuleException("Khóa thanh toán phải gắn với chuyến.", ErrorKind.Validation);
        request = request with
        {
            Amount = decimal.Round(request.Amount, 0),
            PaymentToken = request.Method == PaymentMethod.Wallet ? null : request.PaymentToken,
        };
        var hash = RequestKeys.Hash(
            JsonSerializer.Serialize(
                new
                {
                    request.TripId,
                    request.RiderId,
                    request.Amount,
                    request.Method,
                    request.PaymentToken,
                },
                ApplicationJson.Options
            )
        );
        await using var tx = await unit.Begin(ct);
        await writer.Lock(request.TripId, ct);
        var payment = await writer.FindTracked(request.TripId, ct);
        if (payment is not null)
        {
            RequestKeys.Same(payment.RequestHash, hash);
            if (
                payment.CheckoutReference is not null
                && payment.Status == PaymentStatus.Declined
                && request.Attempt > payment.Attempt
            )
            {
                var retry = await factory.Create(request.Method).Capture(request, ct);
                if (retry.CheckoutReference is not null)
                    payment.AwaitCheckout(retry.CheckoutReference, request.Attempt);
            }
            await PublishResult(payment, ct);
            await unit.Save(ct);
            await tx.Commit(ct);
            return payment;
        }
        var method = factory.Create(request.Method);
        var capture = await method.Capture(request, ct);
        payment = Payment.Captured(
            request.TripId,
            request.RiderId,
            request.Amount,
            request.Method,
            hash,
            key,
            request.CorrelationId,
            capture.Success,
            capture.Reason
        );
        writer.Add(payment);
        if (capture.CheckoutReference is not null)
            payment.AwaitCheckout(capture.CheckoutReference, request.Attempt);
        if (capture.Success)
        {
            var lines = Ledger.Charge(method.DebitAccount(payment.RiderId), payment.Amount);
            AddJournal(payment.Id, "Charge", lines);
            if (capture.RequiresCompensation)
            {
                await method.Refund(payment, ct);
                AddJournal(payment.Id, "Refund", Ledger.Reverse(lines));
                payment.Compensate();
            }
        }
        await PublishResult(payment, ct);
        await unit.Save(ct);
        await tx.Commit(ct);
        metrics.Outcome(payment.Status.ToString().ToLowerInvariant());
        return payment;
    }

    public async Task<Payment> Refund(
        Guid id,
        string key,
        RefundRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
        RequestKeys.Validate(key);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
            throw new RuleException("Cần lý do hoàn tiền, tối đa 200 ký tự.", ErrorKind.Validation);
        var hash = RequestKeys.Hash(request.Reason);
        await using var tx = await unit.Begin(ct);
        await writer.Lock(id, ct);
        var payment =
            await writer.FindTracked(id, ct)
            ?? throw new RuleException("Không có giao dịch.", ErrorKind.NotFound);
        if (payment.Status is PaymentStatus.Refunded or PaymentStatus.RefundPending)
        {
            if (payment.RefundKey != key)
                throw new RuleException("Giao dịch đã được hoàn.");
            RequestKeys.Same(payment.RefundHash!, hash);
            return payment;
        }
        if (payment.Status != PaymentStatus.Settled)
            throw new RuleException("Chỉ hoàn khoản đã thanh toán.");
        if (payment.IsTopUp)
            throw new RuleException("Chức năng này chỉ hoàn tiền chuyến.", ErrorKind.Validation);
        if (payment.CheckoutReference is not null)
        {
            payment.RequestRefund(key, hash, request.Reason);
            await unit.Save(ct);
            await tx.Commit(ct);
            return payment;
        }
        await factory.Create(payment.Method).Refund(payment, ct);
        var charge = (await reader.Journals(payment.Id, ct)).Single(x => x.Kind == "Charge");
        AddJournal(payment.Id, "Refund", Ledger.Reverse(charge.Lines));
        payment.Refund(key, hash, request.Reason);
        await bus.Publish(
            new PaymentRefunded(Guid.NewGuid(), id, id, payment.Amount, payment.CorrelationId),
            payment.CorrelationId,
            ct
        );
        await unit.Save(ct);
        await tx.Commit(ct);
        return payment;
    }

    private void AddJournal(Guid id, string kind, List<LedgerLine> lines)
    {
        Ledger.Validate(lines);
        writer.Add(
            new Journal
            {
                PaymentId = id,
                Kind = kind,
                Lines = lines,
            }
        );
    }

    private Task PublishResult(Payment p, CancellationToken ct) =>
        p.Status switch
        {
            PaymentStatus.Settled => bus.Publish(
                new PaymentSettled(Guid.NewGuid(), p.Id, p.Id, p.Amount, "VND", p.CorrelationId),
                p.CorrelationId,
                ct
            ),
            PaymentStatus.Declined or PaymentStatus.Compensated => bus.Publish(
                new PaymentFailed(
                    Guid.NewGuid(),
                    p.Id,
                    p.Reason ?? "Thanh toán bị từ chối.",
                    p.Status == PaymentStatus.Compensated,
                    p.CorrelationId
                ),
                p.CorrelationId,
                ct
            ),
            _ => Task.CompletedTask,
        };
}
