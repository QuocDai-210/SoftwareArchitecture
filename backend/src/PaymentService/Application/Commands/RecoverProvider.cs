using SwiftRide.Payments.Application.Ports;

namespace SwiftRide.Payments.Application;

public sealed class RecoverProvider(IProviderRecovery provider)
{
    public async Task Execute(Guid tripId, Actor actor, CancellationToken ct)
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
        if (!await provider.Recover(tripId, ct))
            throw new RuleException("Không có provider operation.", ErrorKind.NotFound);
    }
}
