namespace SwiftRide.Payments.Infrastructure;

public sealed class ProviderOperation
{
    public string Key { get; set; } = "";
    public decimal Amount { get; set; }
    public string RequestHash { get; set; } = "";
    public string State { get; set; } = "new";
    public int Attempts { get; set; }
    public bool UnknownInjected { get; set; }
    public bool Recovered { get; set; }
}
