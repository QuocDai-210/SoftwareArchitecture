namespace SwiftRide.Contracts;

public sealed record RefundRequest(string Reason, bool CashReturned = false);
