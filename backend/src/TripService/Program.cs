using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Core;
using SwiftRide.Hosting;
using TripService;

if (await Platform.HealthProbe(args)) return;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("trip");
builder.Services.AddDbContext<TripDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Trip")));
builder.Services.AddScoped<TripRepository>();
builder.Services.AddScoped<ITripReader>(s => s.GetRequiredService<TripRepository>());
builder.Services.AddScoped<ITripWriter>(s => s.GetRequiredService<TripRepository>());
builder.AddReliableBus<TripDb>(false, x =>
{
    x.AddConsumer<SettledConsumer>(); x.AddConsumer<FailedConsumer>(); x.AddConsumer<PaymentFaultConsumer>();
});
builder.Services.AddHostedService<TripWorkflow>();
var app = builder.Build();
app.UsePlatform();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TripDb>().Database.MigrateAsync();
app.MapGet("/health/ready", async (TripDb db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();

app.MapPost("/trips", async (CreateTripRequest request, HttpContext http, TripDb db, CancellationToken ct) =>
{
    if (request.Pickup is null || request.Destination is null) throw new RuleException("Cần điểm đón và điểm đến.", 400);
    request.Pickup.Validate(); request.Destination.Validate();
    _ = Routes.Estimate(request.Pickup, request.Destination);
    if (!Enum.IsDefined(request.Method)) throw new RuleException("Phương thức thanh toán không hợp lệ.", 400);
    if (request.Method == PaymentMethod.Card && request.PaymentToken is not ("demo-success" or "demo-decline" or "demo-transient" or "demo-unknown" or "demo-compensate" or "demo-unavailable"))
        throw new RuleException("Chọn token thanh toán demo hợp lệ.", 400);
    if (request.Method == PaymentMethod.Wallet) request = request with { PaymentToken = null };
    if (!app.Environment.IsDevelopment() && request.Method == PaymentMethod.Card && request.PaymentToken != "demo-success")
        throw new RuleException("Kịch bản lỗi chỉ dùng trong Development.", 400);
    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault(); RequestKeys.Validate(key);
    var hash = RequestKeys.Hash(JsonSerializer.Serialize(request, Platform.Json));
    var rider = http.User.Subject();
    var previous = await db.Trips.SingleOrDefaultAsync(x => x.RiderId == rider && x.IdempotencyKey == key, ct);
    if (previous is not null) { RequestKeys.Same(previous.RequestHash, hash); return Results.Ok(Views.Trip(previous)); }
    var trip = new Trip
    {
        Id = Guid.NewGuid(), RiderId = rider, Pickup = request.Pickup, Destination = request.Destination,
        Method = request.Method, PaymentToken = request.PaymentToken, IdempotencyKey = key!, RequestHash = hash,
        CorrelationId = http.Correlation(), History = [new(TripStatus.Requested, DateTimeOffset.UtcNow, null)]
    };
    db.Trips.Add(trip);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException)
    {
        db.ChangeTracker.Clear();
        previous = await db.Trips.SingleOrDefaultAsync(x => x.RiderId == rider && x.IdempotencyKey == key, ct);
        if (previous is null) throw;
        RequestKeys.Same(previous.RequestHash, hash); return Results.Ok(Views.Trip(previous));
    }
    Telemetry.Trips.Add(1);
    return Results.Created($"/trips/{trip.Id}", Views.Trip(trip));
}).RequireAuthorization("rider");

app.MapGet("/trips", async (ClaimsPrincipal user, TripDb db, CancellationToken ct) =>
{
    var id = user.Subject();
    var query = db.Trips.AsNoTracking().Where(x => x.RiderId == id || (x.DriverId == id && x.Status != TripStatus.Requested && x.Status != TripStatus.AwaitingRiderConfirmation && x.Status != TripStatus.ReservingDriver));
    if (user.IsInRole("ops")) query = db.Trips.AsNoTracking();
    return (await query.OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct)).Select(Views.Trip);
}).RequireAuthorization();
app.MapGet("/trips/{id:guid}", async (Guid id, ClaimsPrincipal user, ITripReader reader, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); Views.CanRead(trip, user); return Views.Trip(trip);
}).RequireAuthorization();
app.MapGet("/drivers/me/offers", async (ClaimsPrincipal user, TripDb db, CancellationToken ct) =>
    (await db.Trips.AsNoTracking().Where(x => x.DriverId == user.Subject() && x.Status == TripStatus.AwaitingDriverAcceptance)
        .ToListAsync(ct)).Select(Views.Trip)).RequireAuthorization("driver");

app.MapPost("/trips/{id:guid}/confirm-quote", async (Guid id, ConfirmQuoteRequest request, ClaimsPrincipal user,
    ITripReader reader, ITripWriter writer, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); Views.Rider(trip, user); trip.CheckVersion(request.ExpectedVersion);
    trip.ConfirmQuote(request.QuoteId, DateTimeOffset.UtcNow); await writer.Save(ct); return Views.Trip(trip);
}).RequireAuthorization("rider");
app.MapPost("/trips/{id:guid}/cancel", async (Guid id, VersionRequest request, ClaimsPrincipal user,
    ITripReader reader, ITripWriter writer, IPublishEndpoint bus, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); Views.Rider(trip, user); trip.CheckVersion(request.ExpectedVersion);
    trip.Move(TripStatus.Cancelled, "Rider hủy chuyến.");
    await WorkflowEvents.Release(trip, bus, ct); await writer.Save(ct);
    Telemetry.Outcomes.Add(1, new KeyValuePair<string, object?>("status", "cancelled")); return Views.Trip(trip);
}).RequireAuthorization("rider");
app.MapPost("/trips/{id:guid}/driver-reject", async (Guid id, VersionRequest request, ClaimsPrincipal user,
    ITripReader reader, ITripWriter writer, IPublishEndpoint bus, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); Views.Driver(trip, user); trip.CheckVersion(request.ExpectedVersion);
    trip.RejectDriver(); await WorkflowEvents.Release(trip, bus, ct); await writer.Save(ct); return Views.Trip(trip);
}).RequireAuthorization("driver");
app.MapPost("/trips/{id:guid}/driver-accept", async (Guid id, VersionRequest request, ClaimsPrincipal user,
    ITripReader reader, ITripWriter writer, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); Views.Driver(trip, user); trip.CheckVersion(request.ExpectedVersion);
    if (trip.Deadline <= DateTimeOffset.UtcNow) throw new RuleException("Lời mời đã hết hạn.");
    trip.Move(TripStatus.AcceptingDriver); await writer.Save(ct); return Views.Trip(trip);
}).RequireAuthorization("driver");
foreach (var (route, target) in new[] { ("en-route", TripStatus.EnRoute), ("pickup", TripStatus.PickedUp), ("dropoff", TripStatus.DroppedOff) })
{
    app.MapPost($"/trips/{{id:guid}}/{route}", async (Guid id, VersionRequest request, ClaimsPrincipal user,
        ITripReader reader, ITripWriter writer, IPublishEndpoint bus, CancellationToken ct) =>
    {
        var trip = await reader.Get(id, ct); Views.Driver(trip, user); trip.CheckVersion(request.ExpectedVersion);
        trip.Move(target);
        if (target == TripStatus.DroppedOff)
        {
            trip.Move(TripStatus.PaymentPending);
            await bus.Emit(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
        }
        await writer.Save(ct); return Views.Trip(trip);
    }).RequireAuthorization("driver");
}
app.MapPost("/trips/{id:guid}/retry-payment", async (Guid id, VersionRequest request, ITripReader reader,
    ITripWriter writer, IPublishEndpoint bus, CancellationToken ct) =>
{
    var trip = await reader.Get(id, ct); trip.CheckVersion(request.ExpectedVersion);
    trip.Move(TripStatus.PaymentPending); await bus.Emit(WorkflowEvents.Completed(trip), trip.CorrelationId, ct);
    await writer.Save(ct); return Views.Trip(trip);
}).RequireAuthorization("ops");
app.Run();

static class Views
{
    public static object Trip(Trip t) => new
    {
        t.Id, t.RiderId, t.Pickup, t.Destination, t.Status, t.DriverId, t.Quote, t.Method,
        t.Rejections, t.Deadline, t.CreatedAt, t.UpdatedAt, t.Reason, t.Version, t.History
    };
    public static void CanRead(Trip t, ClaimsPrincipal user)
    {
        if (!user.IsInRole("ops") && t.RiderId != user.Subject() && (t.DriverId != user.Subject() || t.Status is TripStatus.Requested or TripStatus.AwaitingRiderConfirmation or TripStatus.ReservingDriver))
            throw new RuleException("Không có quyền xem chuyến này.", 403);
    }
    public static void Rider(Trip t, ClaimsPrincipal user)
    {
        if (t.RiderId != user.Subject()) throw new RuleException("Chuyến không thuộc Rider này.", 403);
    }
    public static void Driver(Trip t, ClaimsPrincipal user)
    {
        if ((t.DriverId != user.Subject() || t.Status is TripStatus.Requested or TripStatus.AwaitingRiderConfirmation or TripStatus.ReservingDriver)) throw new RuleException("Chuyến chưa giao cho Driver này.", 403);
    }
}
