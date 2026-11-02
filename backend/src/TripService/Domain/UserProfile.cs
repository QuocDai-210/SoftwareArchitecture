using SwiftRide.SharedKernel;

namespace SwiftRide.Trips.Domain;

public sealed class UserProfile
{
    public string Id { get; init; } = "";
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";

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
