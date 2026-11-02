using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class SurgePricing(IPricingStrategy basis) : IPricingStrategy
{
    public decimal Calculate(FareInput f)
    {
        f.Validate();
        return basis.Calculate(f) * f.Surge;
    }
}
