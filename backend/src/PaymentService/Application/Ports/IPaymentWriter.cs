namespace SwiftRide.Payments.Application.Ports;

public interface IPaymentWriter
{
    Task Lock(Guid tripId, CancellationToken ct);
    Task<Payment?> FindTracked(Guid id, CancellationToken ct);
    Task<Payment?> ByKey(string key, CancellationToken ct);
    void Add(Payment payment);
    void Add(Journal journal);
}
