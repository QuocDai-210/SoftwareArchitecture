using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using SwiftRide.Payments.Application.Sandbox;

namespace SwiftRide.Payments.Infrastructure.Sandbox;

public sealed class SandboxVnpayOptions
{
    public bool Enabled { get; set; }
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string ReturnUrl { get; set; } = "http://localhost:3000/payments/return";

    // Fixed for this sandbox proof; never inferred from untrusted forwarded headers.
    public string ClientIp { get; set; } = "127.0.0.1";
}

public sealed class SandboxVnpay(HttpClient http, SandboxVnpayOptions options, TimeProvider clock)
    : ISandboxVnpay
{
    public const string PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    private const string QueryUrl = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";

    public string Checkout(SandboxPayment payment)
    {
        EnsureConfigured();
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = (payment.Amount * 100).ToString(CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = payment.Reference,
            ["vnp_OrderInfo"] = $"SwiftRide sandbox {payment.Reference}",
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = options.ReturnUrl,
            ["vnp_IpAddr"] = options.ClientIp,
            ["vnp_CreateDate"] = VietnamTime(payment.CreatedAt),
            ["vnp_ExpireDate"] = VietnamTime(payment.ExpiresAt),
        };
        var data = Canonical(fields);
        return $"{PaymentUrl}?{data}&vnp_SecureHash={Sign(data)}";
    }

    public SandboxReceipt? Verify(IReadOnlyDictionary<string, string> fields)
    {
        if (
            !options.Enabled
            || string.IsNullOrEmpty(options.HashSecret)
            || fields.GetValueOrDefault("vnp_TmnCode") != options.TmnCode
            || !Matches(Canonical(fields), fields.GetValueOrDefault("vnp_SecureHash"))
        )
        {
            return null;
        }
        return Receipt(fields);
    }

    public async Task<SandboxReceipt?> Query(SandboxPayment payment, CancellationToken ct)
    {
        EnsureConfigured();
        var request = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = Guid.NewGuid().ToString("N"),
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "querydr",
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_TxnRef"] = payment.Reference,
            ["vnp_TransactionDate"] = VietnamTime(payment.CreatedAt),
            ["vnp_CreateDate"] = VietnamTime(clock.GetUtcNow()),
            ["vnp_IpAddr"] = options.ClientIp,
            ["vnp_OrderInfo"] = $"Query sandbox {payment.Reference}",
        };
        string[] requestOrder =
        [
            "vnp_RequestId",
            "vnp_Version",
            "vnp_Command",
            "vnp_TmnCode",
            "vnp_TxnRef",
            "vnp_TransactionDate",
            "vnp_CreateDate",
            "vnp_IpAddr",
            "vnp_OrderInfo",
        ];
        request["vnp_SecureHash"] = Sign(Join(request, requestOrder));
        using var response = await http.PostAsJsonAsync(QueryUrl, request, ct);
        response.EnsureSuccessStatusCode();
        var fields = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
        if (fields is null)
        {
            return null;
        }
        // A rate-limit response only delays queries; it never authorizes a payment mutation.
        if (fields.GetValueOrDefault("vnp_ResponseCode") == "94")
        {
            throw new SandboxQueryDeferredException();
        }
        string[] responseOrder =
        [
            "vnp_ResponseId",
            "vnp_Command",
            "vnp_ResponseCode",
            "vnp_Message",
            "vnp_TmnCode",
            "vnp_TxnRef",
            "vnp_Amount",
            "vnp_BankCode",
            "vnp_PayDate",
            "vnp_TransactionNo",
            "vnp_TransactionType",
            "vnp_TransactionStatus",
            "vnp_OrderInfo",
            "vnp_PromotionCode",
            "vnp_PromotionAmount",
        ];
        if (
            !Matches(Join(fields, responseOrder), fields.GetValueOrDefault("vnp_SecureHash"))
            || fields.GetValueOrDefault("vnp_TmnCode") != options.TmnCode
            || fields.GetValueOrDefault("vnp_TxnRef") != payment.Reference
            || fields.GetValueOrDefault("vnp_Command") is not null and not "" and not "querydr"
            || fields.GetValueOrDefault("vnp_TransactionType") != "01"
            || fields.GetValueOrDefault("vnp_ResponseCode") != "00"
        )
        {
            // Not found, provider errors and unverifiable results are not payment failures.
            return null;
        }
        return Receipt(fields);
    }

    public static string Canonical(IReadOnlyDictionary<string, string> fields) =>
        string.Join(
            "&",
            fields
                .Where(p =>
                    p.Key.StartsWith("vnp_", StringComparison.Ordinal)
                    && p.Key is not "vnp_SecureHash" and not "vnp_SecureHashType"
                    && !string.IsNullOrEmpty(p.Value)
                )
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}")
        );

    private static SandboxReceipt? Receipt(IReadOnlyDictionary<string, string> fields)
    {
        var reference = fields.GetValueOrDefault("vnp_TxnRef");
        var status = fields.GetValueOrDefault("vnp_TransactionStatus");
        var response = fields.GetValueOrDefault("vnp_ResponseCode");
        var transaction = fields.GetValueOrDefault("vnp_TransactionNo");
        var rawAmount = fields.GetValueOrDefault("vnp_Amount");
        if (
            reference is null
            || reference.Length != 32
            || !reference.All(Uri.IsHexDigit)
            || status is null
            || status.Length != 2
            || response is null
            || response.Length != 2
            || transaction is null
            || transaction.Length is < 1 or > 15
            || !transaction.All(char.IsAsciiDigit)
            || !long.TryParse(
                rawAmount,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var amount
            )
            || amount <= 0
            || amount % 100 != 0
        )
        {
            return null;
        }
        return new SandboxReceipt(reference, amount / 100, transaction, status, response);
    }

    private static string Join(IReadOnlyDictionary<string, string> fields, string[] order) =>
        string.Join("|", order.Select(key => fields.GetValueOrDefault(key, "")));

    private string Sign(string data) =>
        Convert.ToHexStringLower(
            HMACSHA512.HashData(
                Encoding.UTF8.GetBytes(options.HashSecret),
                Encoding.UTF8.GetBytes(data)
            )
        );

    private bool Matches(string data, string? hash)
    {
        if (hash is null || hash.Length != 128 || !hash.All(Uri.IsHexDigit))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Sign(data)),
            Convert.FromHexString(hash)
        );
    }

    private static string VietnamTime(DateTimeOffset value) =>
        value
            .ToOffset(TimeSpan.FromHours(7))
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private void EnsureConfigured()
    {
        if (
            !options.Enabled
            || string.IsNullOrWhiteSpace(options.TmnCode)
            || string.IsNullOrWhiteSpace(options.HashSecret)
            || !Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !IPAddress.TryParse(options.ClientIp, out _)
        )
        {
            throw new RuleException("VNPAY sandbox chưa được cấu hình.", ErrorKind.Unavailable);
        }
    }
}
