using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Core;
using SwiftRide.Hosting;

namespace PaymentService;

public sealed class PaymentApplication(PayDb db, PaymentMethodFactory factory, IPublishEndpoint bus)
{
    public async Task<Payment> Charge(PaymentRequest request, string key, CancellationToken ct)
    {
        RequestKeys.Validate(key);
        if (request.TripId == Guid.Empty || string.IsNullOrWhiteSpace(request.RiderId) || request.RiderId.Length > 100 ||
            request.Amount is < 0 or > 100000000 || request.Amount != decimal.Round(request.Amount, 0))
            throw new RuleException("Yêu cầu thanh toán không hợp lệ.", 400);
        if (key != $"trip:{request.TripId}:charge") throw new RuleException("Khóa thanh toán phải gắn với chuyến.", 400);
        request = request with { Amount = decimal.Round(request.Amount, 0), PaymentToken = request.Method == PaymentMethod.Wallet ? null : request.PaymentToken };
        var hash = RequestKeys.Hash(JsonSerializer.Serialize(new { request.TripId, request.RiderId, request.Amount, request.Method, request.PaymentToken }, Platform.Json));
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({request.TripId.ToString()}, 0))", ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == request.TripId, ct);
        if (payment is not null)
        {
            RequestKeys.Same(payment.RequestHash, hash);
            await PublishResult(payment, ct); await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return payment;
        }
        var method = factory.Create(request.Method);
        var capture = await method.Capture(request, ct);
        payment = new Payment { Id = request.TripId, RiderId = request.RiderId, Amount = request.Amount,
            Method = request.Method, RequestHash = hash, IdempotencyKey = key, CorrelationId = request.CorrelationId,
            Status = capture.Success ? PaymentStatus.Settled : PaymentStatus.Declined, Reason = capture.Reason };
        db.Payments.Add(payment);
        if (capture.Success)
        {
            var lines = Ledger.Charge(payment.Method, payment.RiderId, payment.Amount);
            AddJournal(payment.Id, "Charge", lines);
            if (request.PaymentToken == "demo-compensate")
            {
                await method.Refund(payment, ct);
                AddJournal(payment.Id, "Refund", Ledger.Reverse(lines));
                payment.Status = PaymentStatus.Compensated;
                payment.Reason = "Saga demo thất bại sau thu tiền; khoản thu đã được hoàn.";
            }
        }
        await PublishResult(payment, ct); await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        Telemetry.Payments.Add(1, new KeyValuePair<string, object?>("status", payment.Status.ToString().ToLowerInvariant()));
        return payment;
    }
    public async Task<Payment> Refund(Guid id, string key, RefundRequest request, CancellationToken ct)
    {
        RequestKeys.Validate(key);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
            throw new RuleException("Cần lý do hoàn tiền, tối đa 200 ký tự.", 400);
        var hash = RequestKeys.Hash(request.Reason);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("Không có giao dịch.", 404);
        if (payment.Status == PaymentStatus.Refunded)
        {
            if (payment.RefundKey != key) throw new RuleException("Giao dịch đã được hoàn.");
            RequestKeys.Same(payment.RefundHash!, hash); return payment;
        }
        if (payment.Status != PaymentStatus.Settled) throw new RuleException("Chỉ hoàn khoản đã thanh toán.");
        await factory.Create(payment.Method).Refund(payment, ct);
        AddJournal(payment.Id, "Refund", Ledger.Reverse(Ledger.Charge(payment.Method, payment.RiderId, payment.Amount)));
        payment.Status = PaymentStatus.Refunded; payment.RefundKey = key; payment.RefundHash = hash; payment.Reason = request.Reason;
        await bus.Emit(new PaymentRefunded(Guid.NewGuid(), id, id, payment.Amount, payment.CorrelationId), payment.CorrelationId, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return payment;
    }
    private void AddJournal(Guid id, string kind, List<LedgerLine> lines)
    {
        Ledger.Validate(lines); db.Journals.Add(new Journal { PaymentId = id, Kind = kind, Lines = lines });
    }
    private Task PublishResult(Payment p, CancellationToken ct) => p.Status switch
    {
        PaymentStatus.Settled => bus.Emit(new PaymentSettled(Guid.NewGuid(), p.Id, p.Id, p.Amount, "VND", p.CorrelationId), p.CorrelationId, ct),
        PaymentStatus.Declined or PaymentStatus.Compensated => bus.Emit(new PaymentFailed(Guid.NewGuid(), p.Id,
            p.Reason ?? "Thanh toán bị từ chối.", p.Status == PaymentStatus.Compensated, p.CorrelationId), p.CorrelationId, ct),
        _ => Task.CompletedTask
    };
}
public sealed class RideCompletedConsumer(PaymentApplication payments) : IConsumer<RideCompleted>
{
    public Task Consume(ConsumeContext<RideCompleted> context)
    {
        var m = context.Message;
        return payments.Charge(new(m.TripId, m.RiderId, m.Amount, m.Method, m.PaymentToken, m.CorrelationId),
            $"trip:{m.TripId}:charge", context.CancellationToken);
    }
}
