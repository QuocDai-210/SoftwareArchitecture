using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class PaymentQueries(IPaymentReader reader)
{
    public async Task<PaymentView> Get(Guid id, Actor actor, CancellationToken ct)
    {
        var payment = await Owned(id, actor, ct);
        return PaymentView.From(payment);
    }

    public async Task<IReadOnlyList<JournalView>> Ledger(Guid id, Actor actor, CancellationToken ct)
    {
        await Owned(id, actor, ct);
        return (await reader.Journals(id, ct))
            .Select(j => new JournalView(j.Id, j.PaymentId, j.Kind, j.Lines, j.CreatedAt))
            .ToArray();
    }

    private async Task<Payment> Owned(Guid id, Actor actor, CancellationToken ct)
    {
        var payment =
            await reader.Find(id, ct)
            ?? throw new RuleException("Chưa có giao dịch.", ErrorKind.NotFound);
        if (!actor.IsService && !(actor.IsRider && payment.RiderId == actor.Id))
            throw new RuleException("Không có quyền xem giao dịch này.", ErrorKind.Forbidden);
        return payment;
    }
}
