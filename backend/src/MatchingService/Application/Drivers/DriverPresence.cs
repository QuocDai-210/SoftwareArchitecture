using SwiftRide.Matching.Application.Ports;

namespace SwiftRide.Matching.Application;

public sealed class DriverPresence(IDriverPresence presence, IDriverEligibility eligibility)
{
    public async Task Location(
        Actor actor,
        string name,
        LocationRequest request,
        CancellationToken ct
    )
    {
        Authorize(actor);
        if (!await eligibility.Approved(actor.Id, ct))
            throw new RuleException("Tài xế chưa được duyệt hoặc đã bị khóa.", ErrorKind.Forbidden);
        var point = new Point(request.Latitude, request.Longitude, "Driver");
        point.Validate();
        await presence.Location(actor.Id, name, point, ct);
    }

    public async Task Availability(Actor actor, AvailabilityRequest request, CancellationToken ct)
    {
        Authorize(actor);
        if (!await eligibility.Approved(actor.Id, ct))
            throw new RuleException("Tài xế chưa được duyệt hoặc đã bị khóa.", ErrorKind.Forbidden);
        if (!await presence.Availability(actor.Id, request.Available, ct))
            throw new RuleException("Không tìm thấy tài xế.", ErrorKind.NotFound);
    }

    private static void Authorize(Actor actor)
    {
        if (!actor.IsDriver)
            throw new RuleException("Cần quyền Driver.", ErrorKind.Forbidden);
    }
}
