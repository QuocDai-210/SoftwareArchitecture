using System.Security.Cryptography;
using System.Text;

namespace SwiftRide.SharedKernel;

public static class RequestKeys
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static void Same(string stored, string incoming)
    {
        if (stored != incoming)
            throw new RuleException("Idempotency-Key đã được dùng với nội dung khác.");
    }

    public static void Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(char.IsControl))
            throw new RuleException(
                "Cần Idempotency-Key hợp lệ, tối đa 100 ký tự.",
                ErrorKind.Validation
            );
    }
}
