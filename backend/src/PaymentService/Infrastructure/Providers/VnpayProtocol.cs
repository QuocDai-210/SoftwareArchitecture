using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SwiftRide.Payments.Infrastructure;

public static class VnpayProtocol
{
    public static string Canonical(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Where(x => x.Key.StartsWith("vnp_", StringComparison.Ordinal)
                && x.Key is not ("vnp_SecureHash" or "vnp_SecureHashType") && x.Value.Length > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => WebUtility.UrlEncode(x.Key) + "=" + WebUtility.UrlEncode(x.Value)));

    public static string Sign(string secret, string data) =>
        Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));

    public static bool Verify(string secret, string data, string? signature)
    {
        if (signature?.Length != 128) return false;
        Span<byte> expected = stackalloc byte[64];
        if (!signature.All(char.IsAsciiHexDigit)) return false;
        var actual = Convert.FromHexString(signature);
        HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data), expected);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
