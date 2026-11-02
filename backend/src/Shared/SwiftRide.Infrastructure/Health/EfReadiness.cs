using Microsoft.EntityFrameworkCore;
using SwiftRide.Application.Health;

namespace SwiftRide.Infrastructure;

public sealed class EfReadiness<TDb>(TDb db) : IReadinessProbe
    where TDb : DbContext
{
    public Task<bool> Ready(CancellationToken ct) => db.Database.CanConnectAsync(ct);
}
