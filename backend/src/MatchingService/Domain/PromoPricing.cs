using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class PromoPricing(IPricingStrategy basis) : IPricingStrategy
{
    public decimal Calculate(FareInput f)
    {
        f.Validate();
        return (basis.Calculate(f) + f.Toll) * f.Promotion;
    }
}
