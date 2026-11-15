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
        if (request.Method == PaymentMethod.Card || request.PaymentToken is not null)
            throw new RuleException("Thanh toán thẻ chưa bật; dùng Ví hoặc COD.");
        if (string.IsNullOrWhiteSpace(request.DriverId) || request.DriverId.Length > 100)
            throw new RuleException("Thiếu tài xế của chuyến.", ErrorKind.Validation);
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
        var hash = RequestKeys.Hash(
            JsonSerializer.Serialize(
                new
                {
                    request.TripId,
                    request.RiderId,
                    request.Amount,
                    request.Method,
                    request.PaymentToken,
                    request.DriverId,
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
                payment.Method == PaymentMethod.Cod
                && payment.Status == PaymentStatus.Declined
                && request.Attempt > payment.Attempt
            )
            {
                var retry = await factory.Create(PaymentMethod.Cod).Capture(request, ct);
                payment.AwaitCash(request.DriverId!, request.Attempt);
                if (retry.Outcome == CaptureOutcome.Declined)
                    payment.Decline(retry.Reason ?? "Local COD fixture declined.");
            }
            if (payment.Method == PaymentMethod.Wallet && payment.Status == PaymentStatus.Settled)
            {
                await unit.Save(ct);
                await tx.Commit(ct);
                return payment;
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
            capture.Reason,
            request.DriverId
        );
        if (request.Method == PaymentMethod.Cod)
        {
            payment.AwaitCash(request.DriverId!, request.Attempt);
            if (capture.Outcome == CaptureOutcome.Declined)
                payment.Decline(capture.Reason ?? "Local COD fixture declined.");
        }
        writer.Add(payment);
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
        if (!actor.IsDriver)
            throw new RuleException(
                "Only the assigned Driver can refund a COD trip.",
                ErrorKind.Forbidden
            );
        RequestKeys.Validate(key);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
            throw new RuleException("Cần lý do hoàn tiền, tối đa 200 ký tự.", ErrorKind.Validation);
        if (!request.CashReturned)
            throw new RuleException("Cần xác nhận hoàn COD giả lập.", ErrorKind.Validation);
        var hash = RequestKeys.Hash(request.Reason);
        await using var tx = await unit.Begin(ct);
        await writer.Lock(id, ct);
        var payment = await writer.FindTracked(id, ct)
            ?? throw new RuleException("Không có giao dịch.", ErrorKind.NotFound);
        if (payment.DriverId != actor.Id)
            throw new RuleException("Driver is not assigned to this trip.", ErrorKind.Forbidden);
        if (payment.Method != PaymentMethod.Cod)
        {
            if (payment.Method == PaymentMethod.Wallet)
                throw new RuleException(
                    "Wallet trip refunds are not supported in this simulation."
                );
            throw new RuleException(
                "Giao dịch online cũ chỉ hỗ trợ đọc và đối soát ngoài hệ thống."
            );
        }
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
        await factory.Create(payment.Method).Refund(payment, ct);
        var charge = (await reader.Journals(payment.Id, ct)).Single(x => x.Kind == "Charge");
        AddJournal(payment.Id, "Refund", Ledger.Reverse(charge.Lines));
        payment.Refund(key, hash, request.Reason, actor.Id);
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
                new PaymentSettled(
                    Guid.NewGuid(),
                    p.Id,
                    p.Id,
                    p.Amount,
                    "VND",
                    p.CorrelationId,
                    p.Method == PaymentMethod.Wallet ? p.DriverId : null
                ),
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
