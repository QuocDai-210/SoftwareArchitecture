using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class CashCollection(
    IPaymentWriter writer,
    IUnitOfWork unit,
    PaymentMethodFactory factory,
    IEventPublisher bus,
    IPaymentMetrics metrics
)
{
    public async Task<PaymentView> Confirm(
        Guid id,
        CashCollectionRequest request,
        string key,
        Actor actor,
        CancellationToken ct
    )
    {
        if (!actor.IsDriver && !actor.IsOps)
            throw new RuleException("Cần quyền Driver hoặc Ops.", ErrorKind.Forbidden);
        RequestKeys.Validate(key);
        var reason = request.Reason?.Trim();
        if (
            !request.Received
            || reason?.Length > 200
            || (actor.IsOps && string.IsNullOrEmpty(reason))
        )
            throw new RuleException(
                "Xác nhận đã thu tiền; Ops cần lý do tối đa 200 ký tự.",
                ErrorKind.Validation
            );
        await using var tx = await unit.Begin(ct);
        await writer.Lock(id, ct);
        var payment =
            await writer.FindTracked(id, ct)
            ?? throw new RuleException("Giao dịch đang được tạo. Hãy thử lại.", ErrorKind.NotFound);
        if (!actor.IsOps && payment.DriverId != actor.Id)
            throw new RuleException("Không phải tài xế của chuyến.", ErrorKind.Forbidden);
        var hash = RequestKeys.Hash($"{actor.Id}|{reason}");
        if (payment.CollectCash(key, hash, actor.Id, reason))
        {
            writer.Add(
                new Journal
                {
                    PaymentId = id,
                    Kind = "Charge",
                    Lines = Ledger.Charge(
                        factory.Create(payment.Method).DebitAccount(payment.DriverId!),
                        payment.Amount
                    ),
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
        await unit.Save(ct);
        await tx.Commit(ct);
        metrics.Outcome("cash_confirmed");
        return PaymentView.From(payment);
    }
}
