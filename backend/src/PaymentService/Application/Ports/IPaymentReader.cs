namespace SwiftRide.Payments.Application.Ports;

public interface IPaymentReader
{
    Task<PageResult<PaymentView>> History(string subject, bool all, int page, CancellationToken ct);
    Task<object> Statistics(string subject, bool all, CancellationToken ct);
    Task<Payment?> Find(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Journal>> Journals(Guid paymentId, CancellationToken ct);
    Task<decimal> Balance(string riderId, CancellationToken ct);
}
