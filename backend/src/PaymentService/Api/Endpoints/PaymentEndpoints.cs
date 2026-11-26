namespace SwiftRide.Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/wallets/me",
                async (
                    ClaimsPrincipal user,
                    SwiftRide.Payments.Application.Ports.IWalletAccounts wallets,
                    CancellationToken ct
                ) => new { available = await wallets.Balance(user.Subject(), ct), currency = "VND" }
            )
            .RequireAuthorization("rider");
        app.MapPost(
                "/payments",
                async (
                    PaymentRequest request,
                    HttpContext http,
                    PaymentCommands commands,
                    CancellationToken ct
                ) =>
                    PaymentView.From(
                        await commands.Charge(
                            request with
                            {
                                CorrelationId = http.Correlation(),
                            },
                            http.Request.Headers["Idempotency-Key"].FirstOrDefault()!,
                            ct
                        )
                    )
            )
            .RequireAuthorization("service");
        app.MapGet(
                "/payments/{id:guid}",
                (Guid id, ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.Get(id, user.Actor(), ct)
            )
            .RequireAuthorization("payment-read");
        app.MapGet(
                "/payments/{id:guid}/ledger",
                (Guid id, ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.Ledger(id, user.Actor(), ct)
            )
            .RequireAuthorization("payment-read");
        app.MapPost(
                "/payments/{id:guid}/refund",
                async (
                    Guid id,
                    RefundRequest request,
                    HttpContext http,
                    PaymentCommands commands,
                    CancellationToken ct
                ) =>
                    PaymentView.From(
                        await commands.Refund(
                            id,
                            http.Request.Headers["Idempotency-Key"].FirstOrDefault()!,
                            request,
                            http.User.Actor(),
                            ct
                        )
                    )
            )
            .RequireAuthorization("service");
    }
}
