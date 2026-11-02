using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverAdministration(IDriverAdministration store, IDriverIdentity identity)
{
    private static void Ops(Actor actor)
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
    }

    public Task<PageResult<DriverApplication>> List(Actor actor, int page, CancellationToken ct)
    {
        Ops(actor);
        if (page is < 1 or > 10000)
            throw new RuleException("Trang không hợp lệ.", ErrorKind.Validation);
        return store.Applications(page, ct);
    }

    public async Task<DriverApplication> Create(
        DriverApplication application,
        Actor actor,
        CancellationToken ct
    )
    {
        Ops(actor);
        var prepared = new DriverApplication
        {
            Username = application.Username?.Trim().ToLowerInvariant() ?? "",
            Name = application.Name?.Trim() ?? "",
            Email = application.Email?.Trim() ?? "",
            Phone = application.Phone?.Trim() ?? "",
            Plate = application.Plate?.Trim() ?? "",
            Vehicle = application.Vehicle?.Trim() ?? "",
        };
        prepared.Validate();
        await store.Create(prepared, ct);
        return prepared;
    }

    public async Task<DriverApplication> Approve(
        Guid id,
        string password,
        Actor actor,
        CancellationToken ct
    )
    {
        Ops(actor);
        var application = await store.Application(id, ct);
        if (application.Status == "Approved")
        {
            if (await store.Approved(application.Subject!, ct))
                await identity.Enable(application.Subject!, true, ct);
            return application;
        }
        if (
            string.IsNullOrEmpty(password)
            || password.Length is < 12 or > 100
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
        )
            throw new RuleException(
                "Mật khẩu tạm cần 12–100 ký tự, chữ hoa, chữ thường và số.",
                ErrorKind.Validation
            );
        application.Provision();
        await store.Save(application, ct);
        var subject = await identity.Provision(application, password, ct);
        application.Approve(subject);
        await store.Register(application, ct);
        await store.Save(application, ct);
        await identity.Enable(subject, true, ct);
        return application;
    }

    public async Task<DriverApplication> Reject(
        Guid id,
        string reason,
        Actor actor,
        CancellationToken ct
    )
    {
        Ops(actor);
        var application = await store.Application(id, ct);
        application.Reject(reason);
        await store.Save(application, ct);
        return application;
    }

    public async Task Suspend(string subject, bool suspended, Actor actor, CancellationToken ct)
    {
        Ops(actor);
        await store.Suspend(subject, suspended, ct);
        await identity.Enable(subject, !suspended, ct);
    }
}
