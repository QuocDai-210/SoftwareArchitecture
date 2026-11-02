using System.Data;

namespace SwiftRide.Payments.Infrastructure;

public sealed class DemoCardProvider(
    IServiceScopeFactory scopes,
    SwiftRide.Application.Payments.IPaymentTokenPolicy tokens
) : ICardProvider
{
    public async Task<CaptureResult> Capture(PaymentRequest request, CancellationToken ct)
    {
        var token = request.PaymentToken;
        tokens.Validate(request.Method, token);
        var key = $"trip:{request.TripId}:charge";
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))",
            ct
        );
        var hash = RequestKeys.Hash($"{request.RiderId}|{request.Amount}|{token}");
        var op = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (op is null)
        {
            op = new()
            {
                Key = key,
                Amount = request.Amount,
                RequestHash = hash,
            };
            db.Add(op);
        }
        else
            RequestKeys.Same(op.RequestHash, hash);
        if (op.State == "captured")
            return new(true, RequiresCompensation: token == "demo-compensate");
        if (op.State == "declined")
            return new(false, "Thẻ demo bị từ chối.");
        op.Attempts++;
        var transient =
            (token == "demo-unavailable" && !op.Recovered)
            || (token == "demo-transient" && op.Attempts <= 2);
        if (!transient)
            op.State = token == "demo-decline" ? "declined" : "captured";
        var unknown = token == "demo-unknown" && !op.UnknownInjected;
        if (unknown)
            op.UnknownInjected = true;
        // ponytail: giả lập provider bền vững bằng transaction riêng trong PayDB; thay adapter khi dùng cổng thật.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (transient || unknown)
            throw new TimeoutException("Demo provider timeout; retry with same key.");
        return op.State == "captured"
            ? new(true, RequiresCompensation: token == "demo-compensate")
            : new(false, "Thẻ demo bị từ chối.");
    }

    public async Task Refund(Payment payment, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayDb>();
        var key = $"payment:{payment.Id}:refund";
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"provider:" + key}, 0))",
            ct
        );
        var existing = await db.ProviderOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null)
        {
            if (existing.Amount != payment.Amount)
                throw new RuleException("Refund amount conflict.");
            return;
        }
        var captured = await db.ProviderOperations.AnyAsync(
            x => x.Key == $"trip:{payment.Id}:charge" && x.State == "captured",
            ct
        );
        if (!captured)
            throw new RuleException("Không có khoản thu để hoàn.");
        db.Add(
            new ProviderOperation
            {
                Key = key,
                Amount = payment.Amount,
                State = "refunded",
                RequestHash = RequestKeys.Hash(payment.Id.ToString()),
            }
        );
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
