using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public sealed class UserProfile
{
    public string Id { get; init; } = "";
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    public void SetSeedLocation(double latitude, double longitude)
    {
        if (latitude is < 10.65 or > 10.95 || longitude is < 106.35 or > 107.05)
            throw new RuleException(
                "Vị trí Rider seed nằm ngoài khu vực TP.HCM.",
                ErrorKind.Validation
            );
        Latitude = latitude;
        Longitude = longitude;
    }

    public void Update(string name, string phone)
    {
        var digits = phone?.StartsWith('+') == true ? phone[1..] : phone;
        if (
            string.IsNullOrWhiteSpace(name)
            || name.Length > 100
            || string.IsNullOrWhiteSpace(phone)
            || phone.Length > 15
            || digits!.Length < 9
            || digits.Any(c => !char.IsAsciiDigit(c))
        )
            throw new RuleException(
                "Họ tên hoặc số điện thoại không hợp lệ.",
                ErrorKind.Validation
            );
        Name = name.Trim();
        Phone = phone;
    }
}
