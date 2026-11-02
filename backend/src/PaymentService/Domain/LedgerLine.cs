namespace SwiftRide.Payments.Domain;

public sealed record LedgerLine(string Account, EntrySide Side, decimal Amount);
