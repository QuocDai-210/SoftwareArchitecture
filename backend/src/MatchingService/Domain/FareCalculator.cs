using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class FareCalculator(IPricingStrategy strategy)
{
    public decimal Calculate(FareInput input)
    {
        input.Validate();
        var value = strategy.Calculate(input) * (1 + input.Tax);
        if (value < 0)
            throw new RuleException("Giá không được âm.");
        return decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }
}
