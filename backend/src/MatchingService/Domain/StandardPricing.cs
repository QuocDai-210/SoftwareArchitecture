using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class StandardPricing : IPricingStrategy
{
    public decimal Calculate(FareInput f)
    {
        f.Validate();
        return f.PerKm * f.DistanceKm + f.PerMinute * f.DurationMinutes;
    }
}
