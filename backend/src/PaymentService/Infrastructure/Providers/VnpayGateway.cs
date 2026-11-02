using System.Globalization;
using System.Net.Http.Json;

namespace SwiftRide.Payments.Infrastructure;

public sealed class VnpayGateway(IHttpClientFactory clients, IConfiguration config) : IVnpayGateway
{
    private string Required(string name) =>
        string.IsNullOrWhiteSpace(config["Vnpay:" + name])
            ? throw new RuleException("Chưa cấu hình VNPAY sandbox.", ErrorKind.Unavailable)
            : config["Vnpay:" + name]!;

    private static string Date(DateTimeOffset value) =>
        value
            .ToOffset(TimeSpan.FromHours(7))
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Amount(decimal value) =>
        (value * 100).ToString("0", CultureInfo.InvariantCulture);

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.GetValueOrDefault("vnp_" + key) ?? "";

    private static string Join(IReadOnlyDictionary<string, string> values, params string[] keys) =>
        string.Join("|", keys.Select(k => Value(values, k)));

    public string Checkout(CheckoutAttempt attempt)
    {
        if (attempt.CreatedAt.AddMinutes(15) <= DateTimeOffset.UtcNow)
            throw new RuleException("Checkout đã hết hạn; đối soát trước khi thử lại.");
        var values = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = Required("TmnCode"),
            ["vnp_Amount"] = Amount(attempt.Amount),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = attempt.Reference,
            ["vnp_OrderInfo"] = "SwiftRide " + attempt.Reference,
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = Required("ReturnUrl"),
            ["vnp_IpAddr"] = config["Vnpay:IpAddress"] ?? "127.0.0.1",
            ["vnp_CreateDate"] = Date(attempt.CreatedAt),
            ["vnp_ExpireDate"] = Date(attempt.CreatedAt.AddMinutes(15)),
        };
        var canonical = VnpayProtocol.Canonical(values);
        return "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?"
            + canonical
            + "&vnp_SecureHash="
            + VnpayProtocol.Sign(Required("HashSecret"), canonical);
    }

    public ProviderReceipt Verify(IReadOnlyDictionary<string, string> values)
    {
        if (
            values.Count > 40
            || values.Any(x => x.Key.Length > 100 || x.Value.Length > 1000)
            || !VnpayProtocol.Verify(
                Required("HashSecret"),
                VnpayProtocol.Canonical(values),
                Value(values, "SecureHash")
            )
            || Value(values, "TmnCode") != Required("TmnCode")
        )
            throw new RuleException("Chữ ký VNPAY không hợp lệ.", ErrorKind.Validation);
        var response = Value(values, "ResponseCode");
        var receipt = Receipt(values);
        return response == "00" ? receipt : receipt with { Status = "02" };
    }

    private static ProviderReceipt Receipt(IReadOnlyDictionary<string, string> values)
    {
        if (
            !decimal.TryParse(
                Value(values, "Amount"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var amount
            )
            || amount < 0
            || amount % 100 != 0
            || !Guid.TryParseExact(Value(values, "TxnRef"), "N", out _)
            || Value(values, "TransactionStatus").Length != 2
        )
            throw new RuleException("Dữ liệu VNPAY không hợp lệ.", ErrorKind.Validation);
        return new(
            Value(values, "TxnRef"),
            amount / 100,
            Value(values, "TransactionNo"),
            Value(values, "PayDate"),
            Value(values, "BankCode"),
            Value(values, "TransactionStatus")
        );
    }

    private Dictionary<string, string> Request(
        CheckoutAttempt attempt,
        string command,
        DateTimeOffset now
    ) =>
        new()
        {
            ["vnp_RequestId"] = Guid.NewGuid().ToString("N"),
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = command,
            ["vnp_TmnCode"] = Required("TmnCode"),
            ["vnp_TxnRef"] = attempt.Reference,
            ["vnp_TransactionDate"] = Date(attempt.CreatedAt),
            ["vnp_CreateDate"] = Date(now),
            ["vnp_IpAddr"] = config["Vnpay:IpAddress"] ?? "127.0.0.1",
            ["vnp_OrderInfo"] = "SwiftRide " + command + " " + attempt.Reference,
        };

    private async Task<Dictionary<string, string>> Send(
        Dictionary<string, string> values,
        string[] keys,
        CancellationToken ct
    )
    {
        values["vnp_SecureHash"] = VnpayProtocol.Sign(Required("HashSecret"), Join(values, keys));
        using var response = await clients
            .CreateClient("vnpay")
            .PostAsJsonAsync("merchant_webapi/api/transaction", values, ct);
        if (!response.IsSuccessStatusCode)
            throw new RuleException("VNPAY tạm thời chưa sẵn sàng.", ErrorKind.Unavailable);
        var result =
            await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct)
            ?? throw new RuleException("VNPAY không trả dữ liệu.", ErrorKind.Unavailable);
        var responseKeys = new List<string>
        {
            "ResponseId",
            "Command",
            "ResponseCode",
            "Message",
            "TmnCode",
            "TxnRef",
            "Amount",
            "BankCode",
            "PayDate",
            "TransactionNo",
            "TransactionType",
            "TransactionStatus",
            "OrderInfo",
        };
        if (values["vnp_Command"] == "querydr")
            responseKeys.AddRange(["PromotionCode", "PromotionAmount"]);
        if (
            !VnpayProtocol.Verify(
                Required("HashSecret"),
                Join(result, responseKeys.ToArray()),
                Value(result, "SecureHash")
            )
            || Value(result, "TmnCode") != Required("TmnCode")
            || Value(result, "Command") != values["vnp_Command"]
            || Value(result, "TxnRef") != values["vnp_TxnRef"]
        )
            throw new RuleException("Phản hồi VNPAY không xác thực được.", ErrorKind.Unavailable);
        return result;
    }

    private async Task<Dictionary<string, string>> QueryValues(
        CheckoutAttempt attempt,
        CancellationToken ct
    ) =>
        await Send(
            Request(attempt, "querydr", DateTimeOffset.UtcNow),
            [
                "RequestId",
                "Version",
                "Command",
                "TmnCode",
                "TxnRef",
                "TransactionDate",
                "CreateDate",
                "IpAddr",
                "OrderInfo",
            ],
            ct
        );

    public async Task<ProviderReceipt?> Query(CheckoutAttempt attempt, CancellationToken ct)
    {
        var values = await QueryValues(attempt, ct);
        if (
            Value(values, "ResponseCode") == "91"
            && attempt.CreatedAt.AddMinutes(20) < DateTimeOffset.UtcNow
        )
            return new(attempt.Reference, attempt.Amount, "", "", "", "02");
        if (Value(values, "ResponseCode") != "00" || Value(values, "TransactionType") != "01")
            return null;
        return Receipt(values);
    }

    public async Task<ProviderRefund> Refund(
        CheckoutAttempt attempt,
        string reason,
        bool submit,
        CancellationToken ct
    )
    {
        if (!submit)
        {
            var values = await QueryValues(attempt, ct);
            return new(
                Value(values, "ResponseCode") == "00"
                    && Value(values, "TransactionType") == "02"
                    && Value(values, "TransactionStatus") == "00"
                    && Value(values, "Amount") == Amount(attempt.Amount)
            );
        }
        if (attempt.RefundRequestedAt is null || attempt.ProviderTransaction is null)
            throw new RuleException("Thiếu thông tin hoàn tiền.");
        var request = Request(attempt, "refund", attempt.RefundRequestedAt.Value);
        request["vnp_RequestId"] = attempt.Reference;
        request["vnp_TransactionType"] = "02";
        request["vnp_Amount"] = Amount(attempt.Amount);
        request["vnp_TransactionNo"] = attempt.ProviderTransaction;
        request["vnp_CreateBy"] = "Ops";
        request["vnp_OrderInfo"] =
            "SwiftRide refund " + attempt.Reference + " " + reason.Replace('|', ' ');
        var result = await Send(
            request,
            [
                "RequestId",
                "Version",
                "Command",
                "TmnCode",
                "TransactionType",
                "TxnRef",
                "Amount",
                "TransactionNo",
                "TransactionDate",
                "CreateBy",
                "CreateDate",
                "IpAddr",
                "OrderInfo",
            ],
            ct
        );
        return new(
            Value(result, "ResponseCode") == "00"
                && Value(result, "TransactionType") == "02"
                && Value(result, "TransactionStatus") == "00"
                && Value(result, "Amount") == Amount(attempt.Amount)
        );
    }
}
