using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public interface IPricingStrategy
{
    decimal Calculate(FareInput input);
}
