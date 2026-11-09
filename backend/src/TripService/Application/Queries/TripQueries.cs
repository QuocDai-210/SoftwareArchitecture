using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed class TripQueries(ITripReader reader)
{
    public Task<PageResult<TripView>> History(
        Actor actor,
        int page,
        string? status,
        CancellationToken ct
    )
    {
        if (page is < 1 or > 10000)
            throw new RuleException("Trang không hợp lệ.", ErrorKind.Validation);
        return reader.History(actor.Id, actor.IsOps, page, status, ct);
    }

    public Task<object> Statistics(Actor actor, CancellationToken ct)
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
        return reader.Statistics(ct);
    }

    public async Task<TripWorkflowView> Workflow(Guid id, Actor actor, CancellationToken ct)
    {
        if (!actor.IsOps)
            throw new RuleException("Cần quyền Ops.", ErrorKind.Forbidden);
        var trip = await reader.Get(id, ct);
        return new(
            trip.Id,
            trip.Status,
            trip.WorkflowRetries,
            trip.NextAttemptAt,
            trip.Reason,
            trip.CorrelationId,
            trip.UpdatedAt
        );
    }

    public async Task<TripView> Get(Guid id, Actor actor, CancellationToken ct)
    {
        var trip = await reader.Get(id, ct);
        TripAccess.CanRead(trip, actor);
        return TripView.From(trip);
    }

    public async Task<IEnumerable<TripView>> List(Actor actor, CancellationToken ct) =>
        (await reader.List(actor.Id, actor.IsOps, ct)).Select(TripView.From);

    public async Task<IEnumerable<TripView>> Offers(Actor actor, CancellationToken ct)
    {
        if (!actor.IsDriver)
            throw new RuleException("Cần quyền Driver.", ErrorKind.Forbidden);
        return (await reader.Offers(actor.Id, ct)).Select(TripView.From);
    }
}
