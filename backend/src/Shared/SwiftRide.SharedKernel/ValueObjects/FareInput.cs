namespace SwiftRide.SharedKernel;

public sealed record FareInput(
    decimal DistanceKm,
    decimal DurationMinutes,
    decimal PerKm = 10000,
    decimal PerMinute = 500,
    decimal Surge = 1,
    decimal Toll = 0,
    decimal Promotion = 1,
    decimal Tax = 0.1m
)
{
    public void Validate()
    {
        if (
            DistanceKm is <= 0 or > 100
            || DurationMinutes is <= 0 or > 1440
            || PerKm is < 0 or > 1000000
            || PerMinute is < 0 or > 100000
            || Surge is < 1 or > 5
            || Toll is < 0 or > 10000000
            || Promotion is < 0 or > 1
            || Tax is < 0 or > 1
        )
            throw new RuleException("Thông số tính giá không hợp lệ.", ErrorKind.Validation);
    }
}
