using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverQueries(IDriverReader reader)
{
    public async Task<DriverView> Me(Actor actor, CancellationToken ct)
    {
        if (!actor.IsDriver)
            throw new RuleException("Cần quyền Driver.", ErrorKind.Forbidden);
        var d =
            await reader.Get(actor.Id, ct)
            ?? throw new RuleException("Không tìm thấy tài xế.", ErrorKind.NotFound);
        return new(
            d.Id,
            d.Name,
            d.Available,
            d.Busy,
            d.LastSeenAt,
            d.Position.Latitude,
            d.Position.Longitude
        );
    }
}
