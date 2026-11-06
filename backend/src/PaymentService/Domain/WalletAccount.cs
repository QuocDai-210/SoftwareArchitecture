using SwiftRide.SharedKernel;

namespace SwiftRide.Payments.Domain;

public sealed class WalletAccount
{
    public string OwnerId { get; init; } = "";
    public long Available
    {
        get; private set;
    }
    public long Held
    {
        get; private set;
    }
    public long PendingEarnings
    {
        get; private set;
    }

    public void CreditTopUp(long amount)
    {
        if (amount is < 10000 or > 5000000)
        {
            throw new RuleException("Số tiền nạp không hợp lệ.", ErrorKind.Validation);
        }
        Available = checked(Available + amount);
    }

    public void Hold(long amount)
    {
        if (amount <= 0 || Available < amount)
        {
            throw new RuleException("Số dư ví không đủ để đặt chuyến.", ErrorKind.Validation);
        }
        Available -= amount;
        Held = checked(Held + amount);
    }

    public void HoldWithdrawal(long amount)
    {
        if (amount is < 10000 or > 5000000 || Available < amount)
            throw new RuleException(
                "Số dư khả dụng không đủ để rút khoản tiền này.",
                ErrorKind.Validation
            );
        Available -= amount;
        Held = checked(Held + amount);
    }

    public void Release(long amount)
    {
        if (amount <= 0 || Held < amount)
        {
            throw new RuleException("Khoản giữ ví không hợp lệ.");
        }
        Held -= amount;
        Available = checked(Available + amount);
    }

    public void Settle(long amount)
    {
        if (amount <= 0 || Held < amount)
        {
            throw new RuleException("Khoản giữ ví không hợp lệ.");
        }
        Held -= amount;
    }

    public void CompleteWithdrawal(long amount)
    {
        if (amount <= 0 || Held < amount)
            throw new RuleException("Khoản tiền đang giữ không hợp lệ.");
        Held -= amount;
    }

    public void CreditEarnings(long amount)
    {
        if (amount <= 0)
        {
            throw new RuleException("Doanh thu chuyến không hợp lệ.");
        }
        Available = checked(Available + amount);
    }
}
