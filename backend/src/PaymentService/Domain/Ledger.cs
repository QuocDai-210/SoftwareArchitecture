using SwiftRide.SharedKernel;

namespace SwiftRide.Payments.Domain;

public static class Ledger
{
    public static void Validate(IReadOnlyCollection<LedgerLine> lines)
    {
        if (
            lines.Count < 2
            || lines.Any(x =>
                x.Amount < 0
                || x.Amount != decimal.Round(x.Amount, 0)
                || !Enum.IsDefined(x.Side)
                || string.IsNullOrWhiteSpace(x.Account)
            )
            || lines.Sum(x => x.Side == EntrySide.Debit ? x.Amount : -x.Amount) != 0
        )
            throw new RuleException("Bút toán không cân bằng.");
    }

    public static List<LedgerLine> Charge(string debitAccount, decimal amount)
    {
        if (
            string.IsNullOrWhiteSpace(debitAccount)
            || amount < 0
            || amount != decimal.Round(amount, 0)
        )
            throw new RuleException("Số tiền VND không hợp lệ.", ErrorKind.Validation);
        List<LedgerLine> lines =
        [
            new(debitAccount, EntrySide.Debit, amount),
            new("TripPayable", EntrySide.Credit, amount),
        ];
        Validate(lines);
        return lines;
    }

    public static List<LedgerLine> Reverse(IEnumerable<LedgerLine> lines) =>
        lines
            .Select(x =>
                x with
                {
                    Side = x.Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit,
                }
            )
            .ToList();
}
