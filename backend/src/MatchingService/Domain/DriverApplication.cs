using SwiftRide.SharedKernel;

namespace SwiftRide.Matching.Domain;

public sealed class DriverApplication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Plate { get; init; } = "";
    public string Vehicle { get; init; } = "";
    public string Status { get; private set; } = "Pending";
    public string? Subject { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Phone)
            || Username.Length is < 3 or > 60 || Username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')
            || string.IsNullOrWhiteSpace(Name) || Name.Length > 100
            || Email.Length > 120 || !System.Net.Mail.MailAddress.TryCreate(Email, out _)
            || Phone.Length is < 9 or > 15 || Phone.Any(c => !char.IsAsciiDigit(c) && c != '+')
            || string.IsNullOrWhiteSpace(Plate) || Plate.Length > 20
            || string.IsNullOrWhiteSpace(Vehicle) || Vehicle.Length > 100)
            throw new RuleException("Hồ sơ tài xế không hợp lệ.", ErrorKind.Validation);
    }

    public void Provision()
    {
        if (Status is not ("Pending" or "Provisioning"))
            throw new RuleException("Hồ sơ không ở trạng thái chờ duyệt.");
        Status = "Provisioning";
    }

    public void Approve(string subject)
    {
        if (Status != "Provisioning" || string.IsNullOrWhiteSpace(subject))
            throw new RuleException("Chưa hoàn tất cấp danh tính.");
        Subject = subject; Status = "Approved"; Reason = null;
    }

    public void Reject(string reason)
    {
        if (Status != "Pending" || string.IsNullOrWhiteSpace(reason) || reason.Length > 200)
            throw new RuleException("Cần hồ sơ chờ duyệt và lý do tối đa 200 ký tự.", ErrorKind.Validation);
        Status = "Rejected"; Reason = reason;
    }
}
