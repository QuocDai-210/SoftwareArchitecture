namespace SwiftRide.Matching.Application;

public sealed record PricingSettings(
    decimal Surge = 1,
    decimal Toll = 0,
    decimal Promotion = 1,
    decimal Tax = 0.1m
);

public sealed class QuoteTrip(
    FareCalculator calculator,
    PricingSettings settings,
    TimeProvider clock
)
{
    public Quote Execute(QuoteRequest request)
    {
        if (request.Pickup is null || request.Destination is null)
            throw new RuleException("Cần điểm đón và điểm đến.", ErrorKind.Validation);
        var route = Routes.Estimate(request.Pickup, request.Destination);
        var fare = new FareInput(
            route.DistanceKm,
            route.DurationMinutes,
            Surge: settings.Surge,
            Toll: settings.Toll,
            Promotion: settings.Promotion,
            Tax: settings.Tax
        );
        return new(
            Guid.NewGuid(),
            calculator.Calculate(fare),
            "VND",
            fare,
            clock.GetUtcNow().AddMinutes(2)
        );
    }
}
