using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SwiftRide.Application.Persistence;

namespace SwiftRide.Infrastructure;

public sealed class EfUnitOfWork<TDb>(TDb db) : IUnitOfWork
    where TDb : DbContext
{
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<ITransaction> Begin(CancellationToken ct)
    {
        var current = db.Database.CurrentTransaction;
        return new Transaction(
            current ?? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct),
            current is null
        );
    }

    private sealed class Transaction(IDbContextTransaction transaction, bool owns) : ITransaction
    {
        public Task Commit(CancellationToken ct) =>
            owns ? transaction.CommitAsync(ct) : Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            owns ? transaction.DisposeAsync() : ValueTask.CompletedTask;
    }
}
