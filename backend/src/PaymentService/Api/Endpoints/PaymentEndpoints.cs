namespace SwiftRide.Payments.Api;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
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
            .RequireAuthorization();
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
                "/payments/{id:guid}/confirm-cash",
                (
                    Guid id,
                    CashCollectionRequest request,
                    HttpContext http,
                    CashCollection commands,
                    CancellationToken ct
                ) =>
                    commands.Confirm(
                        id,
                        request,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault()!,
                        http.User.Actor(),
                        ct
                    )
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
    }
}
