namespace SwiftRide.Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/payments/vnpay/ipn",
                async (HttpContext http, HostedPayments payments, CancellationToken ct) =>
                {
                    if (http.Request.Query.Any(x => x.Value.Count != 1))
                        return Results.Json(
                            new Dictionary<string, string>
                            {
                                ["RspCode"] = "97",
                                ["Message"] = "Invalid payload",
                            }
                        );
                    var ack = await payments.Notify(
                        http.Request.Query.ToDictionary(
                            x => x.Key,
                            x => x.Value.ToString(),
                            StringComparer.Ordinal
                        ),
                        ct
                    );
                    return Results.Json(
                        new Dictionary<string, string>
                        {
                            ["RspCode"] = ack.RspCode,
                            ["Message"] = ack.Message,
                        }
                    );
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/payments/by-reference/{reference}",
                (
                    string reference,
                    ClaimsPrincipal user,
                    HostedPayments payments,
                    CancellationToken ct
                ) => payments.ByReference(reference, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/payments/history",
                (int? page, ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.History(page ?? 1, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/payments/statistics",
                (ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.Statistics(user.Actor(), ct)
            )
            .RequireAuthorization("ops");
        app.MapPost(
                "/wallets/me/topups",
                (
                    TopUpRequest request,
                    HttpContext http,
                    HostedPayments payments,
                    CancellationToken ct
                ) =>
                    payments.TopUp(
                        request,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault()!,
                        http.User.Actor(),
                        http.Correlation(),
                        ct
                    )
            )
            .RequireAuthorization("user");
        app.MapGet(
                "/payments/{id:guid}/checkout",
                (Guid id, ClaimsPrincipal user, HostedPayments payments, CancellationToken ct) =>
                    payments.Checkout(id, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapPost(
                "/payments/{id:guid}/reconcile",
                (Guid id, ClaimsPrincipal user, HostedPayments payments, CancellationToken ct) =>
                    payments.Reconcile(id, user.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/wallets/me",
                (ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.Balance(user.Actor(), ct)
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
            .RequireAuthorization();
        app.MapGet(
                "/payments/{id:guid}/ledger",
                (Guid id, ClaimsPrincipal user, PaymentQueries queries, CancellationToken ct) =>
                    queries.Ledger(id, user.Actor(), ct)
            )
            .RequireAuthorization();
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
            .RequireAuthorization("ops");
        app.MapPost(
                "/payments/{id:guid}/demo-recover",
                async (
                    Guid id,
                    ClaimsPrincipal user,
                    RecoverProvider commands,
                    CancellationToken ct
                ) =>
                {
                    await commands.Execute(id, user.Actor(), ct);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization("ops");
    }
}
