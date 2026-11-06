using System.Text.Json.Serialization;
using SwiftRide.Payments.Application.Sandbox;

namespace SwiftRide.Payments.Api;

public sealed record SandboxCreateRequest(long Amount);

public sealed record VnpayAcknowledgement(
    [property: JsonPropertyName("RspCode")] string Code,
    [property: JsonPropertyName("Message")] string Message
);

public static class SandboxEndpoints
{
    public static void MapSandboxEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/payments/sandbox",
                (
                    SandboxCreateRequest request,
                    HttpContext http,
                    SandboxPayments payments,
                    CancellationToken ct
                ) =>
                    payments.Create(
                        request.Amount,
                        http.Request.Headers["Idempotency-Key"].ToString(),
                        http.User.Actor(),
                        ct
                    )
            )
            .RequireAuthorization("user");
        app.MapGet(
                "/payments/sandbox/{reference}",
                (
                    string reference,
                    HttpContext http,
                    SandboxPayments payments,
                    CancellationToken ct
                ) => payments.Get(reference, http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapPost(
                "/payments/sandbox/{reference}/reconcile",
                (
                    string reference,
                    HttpContext http,
                    SandboxPayments payments,
                    CancellationToken ct
                ) => payments.Reconcile(reference, http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/payments/vnpay/ipn",
                async (HttpContext http, SandboxPayments payments, CancellationToken ct) =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    if (
                        http.Request.QueryString.Value?.Length > 8192
                        || http.Request.Query.Any(p => p.Value.Count != 1)
                    )
                    {
                        return Ack("97");
                    }
                    try
                    {
                        var fields = http.Request.Query.ToDictionary(
                            p => p.Key,
                            p => p.Value.ToString()
                        );
                        return Ack(await payments.Notify(fields, ct));
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        return Ack("99");
                    }
                }
            )
            .AllowAnonymous();
    }

    private static VnpayAcknowledgement Ack(string code) =>
        new(
            code,
            code switch
            {
                "00" => "Confirm Success",
                "01" => "Order not found",
                "02" => "Order already confirmed",
                "04" => "Invalid amount",
                "97" => "Invalid signature or payload",
                _ => "Please retry",
            }
        );
}
