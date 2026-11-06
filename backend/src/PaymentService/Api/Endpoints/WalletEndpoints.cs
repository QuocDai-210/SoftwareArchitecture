using SwiftRide.Payments.Application.Sandbox;
using SwiftRide.Payments.Application.Wallets;

namespace SwiftRide.Payments.Api;

public static class WalletEndpoints
{
    public static void MapWalletEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/wallets/me",
                (HttpContext http, WalletQueries queries, CancellationToken ct) =>
                    queries.Get(http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/wallets/history",
                (int? page, HttpContext http, WalletQueries queries, CancellationToken ct) =>
                    queries.History(page ?? 1, http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/wallets/topups",
                (int? page, HttpContext http, WalletQueries queries, CancellationToken ct) =>
                    queries.TopUps(page ?? 1, http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapPost(
                "/wallets/topups",
                (
                    SandboxCreateRequest request,
                    HttpContext http,
                    SandboxPayments payments,
                    CancellationToken ct
                ) =>
                    payments.CreateTopUp(
                        request.Amount,
                        http.Request.Headers["Idempotency-Key"].ToString(),
                        http.User.Actor(),
                        ct
                    )
            )
            .RequireAuthorization("user");
        app.MapGet(
                "/wallets/withdrawals",
                (
                    int? page,
                    HttpContext http,
                    WalletWithdrawals withdrawals,
                    CancellationToken ct
                ) =>
                    withdrawals.History(page ?? 1, http.User.Actor(), ct)
            )
            .RequireAuthorization();
        app.MapPost(
                "/wallets/withdrawals",
                (
                    WalletWithdrawalRequest request,
                    HttpContext http,
                    WalletWithdrawals withdrawals,
                    CancellationToken ct
                ) =>
                    withdrawals.Request(
                        request,
                        http.Request.Headers["Idempotency-Key"].ToString(),
                        http.User.Actor(),
                        ct
                    )
            )
            .RequireAuthorization();
        app.MapGet(
                "/ops/wallet-withdrawals",
                (
                    int? page,
                    HttpContext http,
                    WalletWithdrawals withdrawals,
                    CancellationToken ct
                ) =>
                    withdrawals.Pending(page ?? 1, http.User.Actor(), ct)
            )
            .RequireAuthorization("ops");
        app.MapPost(
                "/ops/wallet-withdrawals/{id:guid}/decision",
                (
                    Guid id,
                    WalletWithdrawalDecision decision,
                    HttpContext http,
                    WalletWithdrawals withdrawals,
                    CancellationToken ct
                ) => withdrawals.Review(id, decision, http.User.Actor(), ct)
            )
            .RequireAuthorization("ops");
    }
}
