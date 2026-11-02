namespace SwiftRide.Payments.Application.Ports;

public interface ICheckoutStore
{
    void Add(CheckoutAttempt attempt);
    Task<CheckoutAttempt?> Find(string reference, CancellationToken ct);
    Task<IReadOnlyList<Guid>> Pending(CancellationToken ct);
}
