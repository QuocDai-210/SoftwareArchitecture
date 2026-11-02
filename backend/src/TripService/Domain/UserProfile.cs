using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public sealed class UserProfile
{
    public string Id { get; init; } = "";
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";

    public void Update(string name, string phone)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || phone.Length is < 9 or > 15
            || phone.Any(c => !char.IsAsciiDigit(c) && c != '+'))
            throw new RuleException("Họ tên hoặc số điện thoại không hợp lệ.", ErrorKind.Validation);
        Name = name.Trim(); Phone = phone;
    }
}
