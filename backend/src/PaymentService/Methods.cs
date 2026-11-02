using System.Data;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Core;

namespace PaymentService;

public sealed record CaptureResult(bool Success, string? Reason = null);
public interface IPaymentMethod
{
    Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct);
    Task Refund(Payment payment, CancellationToken ct);
}
public sealed class WalletMethod(PayDb db) : IPaymentMethod
{
    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        var wallet = await db.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"RiderId\" = {request.RiderId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (wallet is null || wallet.Balance < request.Amount) return new(false, "Ví không đủ tiền.");
        wallet.Balance -= request.Amount; return new(true);
    }
    public async Task Refund(Payment payment, CancellationToken ct)
    {
        var wallet = await db.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"RiderId\" = {payment.RiderId} FOR UPDATE").SingleAsync(ct);
        wallet.Balance += payment.Amount;
    }
}
public sealed class CardMethod(DemoCardProvider provider) : IPaymentMethod
{
    public Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct) => provider.Capture(request, ct);
    public Task Refund(Payment payment, CancellationToken ct) => provider.Refund(payment, ct);
}
public sealed class PaymentMethodFactory(CardMethod card, WalletMethod wallet)
{
    public IPaymentMethod Create(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => card, PaymentMethod.Wallet => wallet,
        _ => throw new RuleException("Phương thức thanh toán không hợp lệ.", 400)
    };
}
public sealed class DemoCardProvider(IServiceScopeFactory scopes, IHostEnvironment environment)
{
    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        var token = request.PaymentToken;
        if (token is not ("demo-success" or "demo-decline" or "demo-transient" or "demo-unknown" or "demo-compensate" or "demo-unavailable"))
            throw new RuleException("Token demo không hợp lệ.", 400);
        if (!environment.IsDevelopment() && token is not "demo-success")
            throw new RuleException("Kịch bản lỗi chỉ dùng trong Development.", 400);
        var key = $"trip:{request.TripId}:charge";
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))", ct);
        var hash = RequestKeys.Hash($"{request.RiderId}|{request.Amount}|{token}");
        var op = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (op is null) { op = new() { Key = key, Amount = request.Amount, RequestHash = hash }; db.Add(op); }
        else RequestKeys.Same(op.RequestHash, hash);
        if (op.State == "captured") return new(true);
        if (op.State == "declined") return new(false, "Thẻ demo bị từ chối.");
        op.Attempts++;
        var transient = (token == "demo-unavailable" && !op.Recovered) || (token == "demo-transient" && op.Attempts <= 2);
        if (!transient) op.State = token == "demo-decline" ? "declined" : "captured";
        var unknown = token == "demo-unknown" && !op.UnknownInjected;
        if (unknown) op.UnknownInjected = true;
        // ponytail: giả lập provider bền vững bằng transaction riêng trong PayDB; thay adapter khi dùng cổng thật.
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (transient || unknown) throw new TimeoutException("Demo provider timeout; retry with same key.");
        return op.State == "captured" ? new(true) : new(false, "Thẻ demo bị từ chối.");
    }
    public async Task Refund(Payment payment, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        var key = $"payment:{payment.Id}:refund";
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))", ct);
        var existing = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null) { if (existing.Amount != payment.Amount) throw new RuleException("Refund amount conflict."); return; }
        var captured = await db.ProviderOperations.AnyAsync(x => x.Key == $"trip:{payment.Id}:charge" && x.State == "captured", ct);
        if (!captured) throw new RuleException("Không có khoản thu để hoàn.");
        db.Add(new ProviderOperation { Key = key, Amount = payment.Amount, State = "refunded", RequestHash = RequestKeys.Hash(payment.Id.ToString()) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
