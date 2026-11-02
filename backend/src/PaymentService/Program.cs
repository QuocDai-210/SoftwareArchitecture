using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PaymentService;
using SwiftRide.Core;
using SwiftRide.Hosting;

if (await Platform.HealthProbe(args)) return;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform("payment");
builder.Services.AddDbContext<PayDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Payment")));
builder.Services.AddScoped<DemoCardProvider>(); builder.Services.AddScoped<CardMethod>();
builder.Services.AddScoped<WalletMethod>(); builder.Services.AddScoped<PaymentMethodFactory>(); builder.Services.AddScoped<PaymentApplication>();
builder.AddReliableBus<PayDb>(true, x => x.AddConsumer<RideCompletedConsumer>());
var app = builder.Build(); app.UsePlatform();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PayDb>(); await db.Database.MigrateAsync();
    foreach (var rider in new[] { "11111111-1111-1111-1111-000000000001", "11111111-1111-1111-1111-000000000002" })
    {
        if (await db.Wallets.AnyAsync(x => x.RiderId == rider)) continue;
        db.Wallets.Add(new Wallet { RiderId = rider, Balance = 2000000 });
        db.Journals.Add(new Journal { PaymentId = Guid.Empty, Kind = $"Seed:{rider}", Lines =
            [new("DemoFunding", EntrySide.Debit, 2000000), new($"WalletLiability:{rider}", EntrySide.Credit, 2000000)] });
        await db.SaveChangesAsync();
    }
}
app.MapGet("/health/ready", async (PayDb db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();
app.MapGet("/wallets/me", async (ClaimsPrincipal user, PayDb db, CancellationToken ct) =>
{
    var wallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(x => x.RiderId == user.Subject(), ct);
    return Results.Ok(new { balance = wallet?.Balance ?? 0, currency = "VND" });
}).RequireAuthorization("rider");
app.MapPost("/payments", async (PaymentRequest request, HttpContext http, PaymentApplication payments, CancellationToken ct) =>
{
    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault(); RequestKeys.Validate(key);
    return PaymentView(await payments.Charge(request with { CorrelationId = http.Correlation() }, key!, ct));
}).RequireAuthorization("service");
app.MapGet("/payments/{id:guid}", async (Guid id, ClaimsPrincipal user, PayDb db, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("Chưa có giao dịch.", 404);
    CanRead(p, user); return PaymentView(p);
}).RequireAuthorization();
app.MapGet("/payments/{id:guid}/ledger", async (Guid id, ClaimsPrincipal user, PayDb db, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new RuleException("Chưa có giao dịch.", 404);
    CanRead(p, user); return await db.Journals.AsNoTracking().Where(x => x.PaymentId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
}).RequireAuthorization();
app.MapPost("/payments/{id:guid}/refund", async (Guid id, RefundRequest request, HttpContext http, PaymentApplication payments, CancellationToken ct) =>
{
    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault(); RequestKeys.Validate(key);
    return PaymentView(await payments.Refund(id, key!, request, ct));
}).RequireAuthorization("ops");
app.MapPost("/payments/{id:guid}/demo-recover", async (Guid id, PayDb db, IHostEnvironment env, CancellationToken ct) =>
{
    if (!env.IsDevelopment()) return Results.NotFound();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var key = $"trip:{id}:charge";
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))", ct);
    var op = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
    if (op is null) return Results.NotFound();
    op.Recovered = true; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.NoContent();
}).RequireAuthorization("ops");
app.Run();

static object PaymentView(Payment p) => new { p.Id, tripId = p.Id, p.RiderId, p.Amount, currency = "VND", p.Method, p.Status, p.Reason, p.CreatedAt };
static void CanRead(Payment p, ClaimsPrincipal user)
{
    if (!user.IsInRole("service") && !user.IsInRole("ops") && p.RiderId != user.Subject())
        throw new RuleException("Không có quyền xem giao dịch này.", 403);
}
