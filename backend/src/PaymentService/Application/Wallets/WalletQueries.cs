using SwiftRide.Payments.Application.Sandbox;

namespace SwiftRide.Payments.Application.Wallets;

public sealed class WalletQueries(IWalletAccounts accounts)
{
    public async Task<WalletView> Get(Actor actor, CancellationToken ct)
    {
        RequireOwner(actor);
        var wallet = await accounts.Find(actor.Id, ct);
        return new(
            wallet?.Available ?? 0,
            wallet?.Held ?? 0,
            wallet?.PendingEarnings ?? 0,
            "VND",
            actor.IsRider && !actor.IsDriver && !actor.IsOps,
            (actor.IsRider || actor.IsDriver) && !actor.IsOps && !actor.IsService
        );
    }

    public Task<PageResult<WalletActivity>> History(int page, Actor actor, CancellationToken ct)
    {
        RequireOwner(actor);
        return accounts.History(actor.Id, Page(page), ct);
    }

    public Task<PageResult<SandboxView>> TopUps(int page, Actor actor, CancellationToken ct)
    {
        RequireOwner(actor);
        return accounts.TopUps(actor.Id, Page(page), ct);
    }

    private static void RequireOwner(Actor actor)
    {
        if ((!actor.IsRider && !actor.IsDriver) || actor.IsOps || actor.IsService)
        {
            throw new RuleException("Ví dành cho tài khoản User hoặc Driver.", ErrorKind.Forbidden);
        }
    }

    private static int Page(int page)
    {
        if (page is < 1 or > 100000)
        {
            throw new RuleException("Trang không hợp lệ.", ErrorKind.Validation);
        }
        return page;
    }
}
