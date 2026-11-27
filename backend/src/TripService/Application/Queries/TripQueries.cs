using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripQueries(ITripReader reader)
{
    public async Task<TripView> Get(Guid id, Actor actor, CancellationToken ct)
    {
        var trip = await reader.Get(id, ct);
        TripAccess.CanRead(trip, actor);
        return TripView.From(trip);
    }

    public async Task<IEnumerable<TripView>> List(Actor actor, CancellationToken ct) =>
        (await reader.List(actor.Id, false, ct)).Select(TripView.From);

    public async Task<IEnumerable<TripView>> Offers(Actor actor, CancellationToken ct)
    {
        if (!actor.IsDriver)
            throw new RuleException("Cần quyền Driver.", ErrorKind.Forbidden);
        return (await reader.Offers(actor.Id, ct)).Select(TripView.From);
    }
}
