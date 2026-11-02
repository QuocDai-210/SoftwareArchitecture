namespace SwiftRide.Application.Persistence;

public interface IUnitOfWork
{
    Task<ITransaction> Begin(CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface ITransaction : IAsyncDisposable
{
    Task Commit(CancellationToken ct);
}
