using System.Text.Json;
using SwiftRide.Trips.Application.Ports;

namespace SwiftRide.Trips.Application;

public sealed record CreatedTrip(TripView View, Guid Id, bool IsNew);

public sealed class TripCommands(
    ITripReader reader,
    ITripWriter writer,
    IEventPublisher bus,
    ITripMetrics metrics
)
{
    public async Task<CreatedTrip> Create(
        CreateTripRequest request,
        string key,
        Actor actor,
        string correlation,
        CancellationToken ct
    )
    {
        if (request.Pickup is null || request.Destination is null)
            throw new RuleException("Cần điểm đón và điểm đến.", ErrorKind.Validation);
        request.Pickup.Validate();
        request.Destination.Validate();
        if (
            request.Pickup.Latitude == request.Destination.Latitude
            && request.Pickup.Longitude == request.Destination.Longitude
        )
            throw new RuleException("Điểm đón và điểm đến phải khác nhau.", ErrorKind.Validation);
        PaymentTokens.Validate(request.Method, request.PaymentToken);
        RequestKeys.Validate(key);
        if (!actor.IsRider)
            throw new RuleException("Cần quyền Rider.", ErrorKind.Forbidden);
        var hash = RequestKeys.Hash(JsonSerializer.Serialize(request, ApplicationJson.Options));
        var rider = actor.Id;
        var previous = await reader.ByBookingKey(rider, key, ct);
        if (previous is not null)
        {
            RequestKeys.Same(previous.RequestHash, hash);
            return new(TripView.From(previous), previous.Id, false);
        }
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RiderId = rider,
            Pickup = request.Pickup,
            Destination = request.Destination,
            Method = request.Method,
            PaymentToken = request.PaymentToken,
            IdempotencyKey = key!,
            RequestHash = hash,
            CorrelationId = correlation,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
        };
        var stored = await writer.Create(trip, ct);
        RequestKeys.Same(stored.RequestHash, hash);
        var created = stored.Id == trip.Id;
        if (created)
            metrics.Requested();
        return new(TripView.From(stored), stored.Id, created);
    }

    public async Task<TripView> ConfirmQuote(
        Guid id,
        ConfirmQuoteRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Rider(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        trip.ConfirmQuote(request.QuoteId, DateTimeOffset.UtcNow);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> Cancel(
        Guid id,
        CancelTripRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Rider(trip, actor);
        var reason = request.Reason?.Trim();
        if (
            (reason?.Length ?? 0) > 240
            || (
                trip.Status is TripStatus.DriverAccepted or TripStatus.EnRoute
                && string.IsNullOrWhiteSpace(reason)
            )
        )
            throw new RuleException("Cần lý do hủy, tối đa 240 ký tự.", ErrorKind.Validation);
        trip.CheckVersion(request.ExpectedVersion);
        trip.Move(TripStatus.Cancelled, reason ?? "Rider cancelled.");
        await WorkflowEvents.Release(trip, bus, ct);
        await writer.Save(ct);
        metrics.Outcome("cancelled");
        return TripView.From(trip);
    }

    public async Task<TripView> RejectDriver(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        await WorkflowEvents.Release(trip, bus, ct);
        trip.RejectDriver();
        await writer.Save(ct);
        if (trip.Status == TripStatus.Failed)
            metrics.Outcome("failed");
        return TripView.From(trip);
    }

    public async Task<TripView> AcceptDriver(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        if (trip.Deadline <= DateTimeOffset.UtcNow)
            throw new RuleException("Lời mời đã hết hạn.");
        trip.Move(TripStatus.AcceptingDriver);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> RetryPayment(
        Guid id,
        VersionRequest request,
        Actor actor,
        CancellationToken ct
    )
    {
        var trip = await reader.Get(id, ct);
        if (!actor.IsService)
            throw new RuleException("Service role required.", ErrorKind.Forbidden);
        if (trip.Status != TripStatus.PaymentReview || trip.Refunded)
            throw new RuleException("Only trips in payment review can be retried.");
        trip.CheckVersion(request.ExpectedVersion);
        trip.Move(TripStatus.PaymentPending);
        await bus.Publish(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
        await writer.Save(ct);
        return TripView.From(trip);
    }

    public async Task<TripView> Move(
        Guid id,
        VersionRequest request,
        Actor actor,
        TripStatus target,
        CancellationToken ct
    )
    {
        if (target is not (TripStatus.EnRoute or TripStatus.PickedUp or TripStatus.DroppedOff))
            throw new RuleException("Thao tác hành trình không hợp lệ.", ErrorKind.Validation);
        var trip = await reader.Get(id, ct);
        TripAccess.Driver(trip, actor);
        trip.CheckVersion(request.ExpectedVersion);
        trip.Move(target);
        if (target == TripStatus.DroppedOff)
        {
            trip.Move(TripStatus.PaymentPending);
            await WorkflowEvents.Release(trip, bus, ct);
            await bus.Publish(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
        }
        await writer.Save(ct);
        return TripView.From(trip);
    }
}
