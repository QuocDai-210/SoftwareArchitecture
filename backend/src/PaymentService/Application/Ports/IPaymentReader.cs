namespace SwiftRide.Payments.Application.Ports;

public interface IPaymentReader
{
    Task<Payment?> Find(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Journal>> Journals(Guid paymentId, CancellationToken ct);
}
