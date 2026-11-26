namespace SwiftRide.SharedKernel;

public enum PaymentMethod
{
    Card,
    Wallet,
}

public static class PaymentTokens
{
    public static void Validate(PaymentMethod method, string? token)
    {
        if (
            !(
                method switch
                {
                    PaymentMethod.Wallet => token is null,
                    PaymentMethod.Card => token is "card-ok" or "card-decline" or "card-timeout",
                    _ => false,
                }
            )
        )
            throw new RuleException("Invalid payment method or test token.", ErrorKind.Validation);
    }
}
