using SwiftRide.SharedKernel;

namespace SwiftRide.Payments.Domain;

public sealed class Wallet
{
    public string RiderId { get; init; } = "";
    public decimal Balance { get; private set; }

    public static Wallet Funded(string riderId, decimal amount)
    {
        var wallet = new Wallet { RiderId = riderId };
        wallet.Credit(amount);
        return wallet;
    }

    public bool TryDebit(decimal amount)
    {
        ValidateAmount(amount);
        if (Balance < amount)
            return false;
        Balance -= amount;
        return true;
    }

    public void Credit(decimal amount)
    {
        ValidateAmount(amount);
        Balance += amount;
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount < 0 || amount != decimal.Round(amount, 0))
            throw new RuleException("Số tiền VND không hợp lệ.", ErrorKind.Validation);
    }
}
