namespace SwiftRide.Payments.Domain;

public sealed class Journal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string Kind { get; set; } = "";
    public List<LedgerLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
