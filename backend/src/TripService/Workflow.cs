using System.Net.Http.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Core;
using SwiftRide.Hosting;

namespace TripService;

public static class WorkflowEvents
{
    public static RideCompleted Completed(Trip trip) => new(Guid.NewGuid(), trip.Id, trip.RiderId,
        trip.Quote!.Amount, trip.Method, trip.PaymentToken, trip.CorrelationId);
    public static Task Release(Trip trip, IPublishEndpoint bus, CancellationToken ct) => trip.DriverId is not null && trip.ReservationId is not null
        ? bus.Emit(new ReservationReleased(Guid.NewGuid(), trip.Id, trip.DriverId, trip.ReservationId.Value, trip.CorrelationId), trip.CorrelationId, ct)
        : Task.CompletedTask;
}
public sealed class TripWorkflow(IServiceScopeFactory scopes, ILogger<TripWorkflow> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TripDb>();
                var now = DateTimeOffset.UtcNow;
                var ids = await db.Trips.AsNoTracking().Where(t => (t.NextAttemptAt == null || t.NextAttemptAt <= now) && (t.Status == TripStatus.Requested ||
                    t.Status == TripStatus.ReservingDriver || t.Status == TripStatus.AcceptingDriver ||
                    (t.Status == TripStatus.AwaitingDriverAcceptance && t.Deadline <= now) ||
                    (t.Status == TripStatus.AwaitingRiderConfirmation && t.Deadline <= now)))
                    .OrderBy(t => t.UpdatedAt).Select(t => t.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try { await Step(id, stoppingToken); }
                    catch (DbUpdateConcurrencyException) { }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Workflow step failed for trip {TripId}", id);
                        try { await RecordFailure(id, Platform.IsTransient(ex), stoppingToken); }
                        catch (Exception saveError) when (!stoppingToken.IsCancellationRequested)
                        { logger.LogWarning(saveError, "Could not persist workflow failure for {TripId}", id); }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Workflow scan deferred"); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
    private async Task RecordFailure(Guid id, bool transient, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDb>();
        var trip = await db.Trips.SingleAsync(t => t.Id == id, ct);
        if (trip.Status is not (TripStatus.Requested or TripStatus.ReservingDriver or TripStatus.AcceptingDriver)) return;
        if (transient && trip.WorkflowRetries < 3)
        {
            trip.WorkflowRetries++;
            trip.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, trip.WorkflowRetries) + Random.Shared.NextDouble());
            trip.Reason = "Dịch vụ tạm thời lỗi. Đang thử lại.";
            trip.Version++; trip.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            trip.Move(TripStatus.Failed, "Không thể hoàn tất ghép xe. Hãy thử tạo chuyến mới.");
            await WorkflowEvents.Release(trip, scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), ct);
            Telemetry.Outcomes.Add(1, new KeyValuePair<string, object?>("status", "failed"));
        }
        await db.SaveChangesAsync(ct);
    }
    private async Task Step(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDb>();
        var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var trip = await db.Trips.SingleAsync(x => x.Id == id, ct);
        using var activity = Telemetry.Activities.StartActivity("trip.workflow");
        activity?.SetTag("trip.id", id); activity?.SetTag("correlation.id", trip.CorrelationId);
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("matching");
        client.DefaultRequestHeaders.Add("CorrelationId", trip.CorrelationId);
        switch (trip.Status)
        {
            case TripStatus.Requested:
            {
                using var response = await client.PostAsJsonAsync("/match", new MatchRequest(trip.Pickup, trip.ExcludedDrivers.ToArray()), Platform.Json, ct);
                var match = (await response.Read<MatchResponse>(ct)).Driver;
                if (match is null) { trip.Move(TripStatus.Failed, "Không có tài xế phù hợp trong 5 km."); break; }
                using var quoteResponse = await client.PostAsJsonAsync("/quote", new QuoteRequest(trip.Pickup, trip.Destination), Platform.Json, ct);
                trip.Quote = await quoteResponse.Read<Quote>(ct);
                trip.DriverId = match.DriverId; trip.Deadline = trip.Quote.ExpiresAt;
                trip.Move(TripStatus.AwaitingRiderConfirmation); break;
            }
            case TripStatus.ReservingDriver:
            {
                using var response = await client.PostAsJsonAsync("/reservations", new ReservationRequest(trip.Id, trip.ReservationId!.Value, trip.DriverId!), Platform.Json, ct);
                var reservation = await response.Read<ReservationResult>(ct);
                if (reservation.Acquired)
                {
                    trip.Deadline = reservation.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(60);
                    trip.Move(TripStatus.AwaitingDriverAcceptance);
                }
                else
                {
                    trip.ExcludedDrivers.Add(trip.DriverId!);
                    trip.Move(TripStatus.Requested, "Tài xế vừa nhận chuyến khác. Đang tìm lại.");
                }
                break;
            }
            case TripStatus.AcceptingDriver:
            {
                using var response = await client.PostAsJsonAsync("/reservations/confirm", new ReservationRequest(trip.Id, trip.ReservationId!.Value, trip.DriverId!), Platform.Json, ct);
                var accepted = await response.Read<ReservationResult>(ct);
                if (accepted.Acquired)
                {
                    trip.Deadline = null; trip.Move(TripStatus.DriverAccepted);
                    await bus.Emit(new TripAccepted(Guid.NewGuid(), trip.Id, trip.DriverId!, trip.ReservationId.Value, trip.CorrelationId), trip.CorrelationId, ct);
                }
                else
                {
                    trip.ExcludedDrivers.Add(trip.DriverId!); trip.Move(TripStatus.Requested, "Lời mời đã hết hiệu lực.");
                    await WorkflowEvents.Release(trip, bus, ct);
                }
                break;
            }
            case TripStatus.AwaitingDriverAcceptance when trip.Deadline <= DateTimeOffset.UtcNow:
                trip.RejectDriver(); await WorkflowEvents.Release(trip, bus, ct); break;
            case TripStatus.AwaitingRiderConfirmation when trip.Deadline <= DateTimeOffset.UtcNow:
                trip.Move(TripStatus.Requested, "Báo giá hết hạn. Đang lấy giá mới."); break;
            default: return;
        }
        trip.WorkflowRetries = 0; trip.NextAttemptAt = null;
        await db.SaveChangesAsync(ct);
        if (trip.Status == TripStatus.Failed) Telemetry.Outcomes.Add(1, new KeyValuePair<string, object?>("status", "failed"));
    }
}
public sealed class SettledConsumer(TripDb db) : IConsumer<PaymentSettled>
{
    public async Task Consume(ConsumeContext<PaymentSettled> context)
    {
        var m = context.Message;
        var trip = await db.Trips.SingleAsync(t => t.Id == m.TripId, context.CancellationToken);
        if (trip.Status == TripStatus.Paid) return;
        if (m.PaymentId != trip.Id || m.Currency != "VND" || m.Amount != trip.Quote?.Amount)
            throw new RuleException("PaymentSettled không khớp chuyến.");
        trip.Move(TripStatus.Paid); await db.SaveChangesAsync(context.CancellationToken);
        Telemetry.Outcomes.Add(1, new KeyValuePair<string, object?>("status", "paid"));
    }
}
public sealed class FailedConsumer(TripDb db) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var trip = await db.Trips.SingleAsync(t => t.Id == context.Message.TripId, context.CancellationToken);
        if (trip.Status != TripStatus.PaymentPending) return;
        trip.Move(TripStatus.PaymentReview, context.Message.Reason); await db.SaveChangesAsync(context.CancellationToken);
    }
}
public sealed class PaymentFaultConsumer(TripDb db) : IConsumer<Fault<RideCompleted>>
{
    public async Task Consume(ConsumeContext<Fault<RideCompleted>> context)
    {
        var trip = await db.Trips.SingleAsync(t => t.Id == context.Message.Message.TripId, context.CancellationToken);
        if (trip.Status != TripStatus.PaymentPending) return;
        trip.Move(TripStatus.PaymentReview, "Thanh toán hết lượt retry; cần đối soát hoặc phát lại.");
        Telemetry.Payments.Add(1, new KeyValuePair<string, object?>("status", "error"));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
