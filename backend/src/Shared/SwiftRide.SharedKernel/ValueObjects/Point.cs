namespace SwiftRide.SharedKernel;

public sealed record Point(double Latitude, double Longitude, string Label)
{
    public void Validate()
    {
        if (
            !double.IsFinite(Latitude)
            || !double.IsFinite(Longitude)
            || Latitude is < -90 or > 90
            || Longitude is < -180 or > 180
            || string.IsNullOrWhiteSpace(Label)
            || Label.Length > 120
        )
            throw new RuleException("Điểm đón/đến không hợp lệ.", ErrorKind.Validation);
    }
}
