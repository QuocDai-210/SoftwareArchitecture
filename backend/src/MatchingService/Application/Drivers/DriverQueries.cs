using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverQueries(IDriverReader reader)
{
    public Task<DriverContact?> Contact(string id, Actor actor, CancellationToken ct)
    {
        if (!actor.IsService)
            throw new RuleException("API chỉ dành cho service.", ErrorKind.Forbidden);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            throw new RuleException("Mã tài xế không hợp lệ.", ErrorKind.Validation);
        return reader.Contact(id, ct);
    }

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
