using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverAdministration(IDriverAdministration store, IDriverIdentity identity)
{
    private static void Ops(Actor actor)
    {
        if (!actor.IsOps) throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
    }

    public Task<IReadOnlyList<DriverApplication>> List(Actor actor, CancellationToken ct)
    {
        Ops(actor); return store.Applications(ct);
    }

    public async Task<DriverApplication> Create(DriverApplication application, Actor actor, CancellationToken ct)
    {
        Ops(actor); application.Validate(); await store.Create(application, ct); return application;
    }

    public async Task<DriverApplication> Approve(Guid id, string password, Actor actor, CancellationToken ct)
    {
        Ops(actor);
        var application = await store.Application(id, ct);
        if (application.Status == "Approved") { if (await store.Approved(application.Subject!, ct)) await identity.Enable(application.Subject!, true, ct); return application; }
        if (string.IsNullOrEmpty(password) || password.Length is < 12 or > 100 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            throw new RuleException("Mật khẩu tạm cần 12–100 ký tự, chữ hoa, chữ thường và số.", ErrorKind.Validation);
        application.Provision(); await store.Save(application, ct);
        var subject = await identity.Provision(application, password, ct);
        application.Approve(subject);
        await store.Register(application, ct);
        await store.Save(application, ct);
        await identity.Enable(subject, true, ct);
        return application;
    }

    public async Task<DriverApplication> Reject(Guid id, string reason, Actor actor, CancellationToken ct)
    {
        Ops(actor); var application = await store.Application(id, ct);
        application.Reject(reason); await store.Save(application, ct); return application;
    }

    public async Task Suspend(string subject, bool suspended, Actor actor, CancellationToken ct)
    {
        Ops(actor); await store.Suspend(subject, suspended, ct);
        await identity.Enable(subject, !suspended, ct);
    }
}
