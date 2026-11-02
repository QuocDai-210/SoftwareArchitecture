namespace SwiftRide.Payments.Application;

public sealed record JournalView(
    Guid Id,
    Guid PaymentId,
    string Kind,
    IReadOnlyList<LedgerLine> Lines,
    DateTimeOffset CreatedAt
);
