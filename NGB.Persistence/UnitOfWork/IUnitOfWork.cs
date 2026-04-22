using System.Data.Common;

namespace NGB.Persistence.UnitOfWork;

public interface IUnitOfWork : IAsyncDisposable
{
    DbConnection Connection { get; }
    DbTransaction? Transaction { get; }
    bool HasActiveTransaction { get; }

    Task EnsureConnectionOpenAsync(CancellationToken ct = default);
    
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    void EnsureActiveTransaction();
}
