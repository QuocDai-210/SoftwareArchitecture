using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class PaymentQueries(IPaymentReader reader)
{
    public Task<PageResult<PaymentView>> History(int page, Actor actor, CancellationToken ct)
    {
        if (!actor.IsRider && !actor.IsDriver && !actor.IsOps)
            throw new RuleException("Cần quyền User, Driver hoặc Ops.", ErrorKind.Forbidden);
        if (page is < 1 or > 10000)
            throw new RuleException("Trang không hợp lệ.", ErrorKind.Validation);
        return reader.History(actor.Id, actor.IsOps, page, ct);
    }

    public Task<object> Statistics(Actor actor, CancellationToken ct)
    {
        if (!actor.IsOps && !actor.IsDriver && !actor.IsRider)
            throw new RuleException("Không có quyền xem thống kê.", ErrorKind.Forbidden);
        return reader.Statistics(actor.Id, actor.IsOps, ct);
    }

    public async Task<WalletBalance> Balance(Actor actor, CancellationToken ct)
    {
        if (!actor.IsRider)
            throw new RuleException("Cần quyền User.", ErrorKind.Forbidden);
        return new(await reader.Balance(actor.Id, ct), "VND");
    }

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
        if (
            !actor.IsService
            && !actor.IsOps
            && payment.RiderId != actor.Id
            && !(actor.IsDriver && payment.DriverId == actor.Id)
        )
            throw new RuleException("Không có quyền xem giao dịch này.", ErrorKind.Forbidden);
        return payment;
    }
}
