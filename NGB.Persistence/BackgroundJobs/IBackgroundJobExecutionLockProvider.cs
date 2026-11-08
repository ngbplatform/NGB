namespace NGB.Persistence.BackgroundJobs;

/// <summary>
/// Additional execution lock held for the entire job run, independently of the scheduler's lease.
/// Connection details refer to the scheduler database, which may differ from the application database.
/// </summary>
public sealed record BackgroundJobExecutionLockRequest(
    string ConnectionString,
    string StorageNamespace,
    string Resource,
    TimeSpan Timeout);

public interface IBackgroundJobExecutionLockProvider
{
    /// <summary>Returns null when another run holds the lock until the acquisition timeout.</summary>
    Task<IBackgroundJobExecutionLock?> TryAcquireAsync(
        BackgroundJobExecutionLockRequest request,
        CancellationToken ct = default);
}

public interface IBackgroundJobExecutionLock : IAsyncDisposable
{
    /// <summary>Cancelled if ownership is lost before disposal. The running job must stop cooperatively.</summary>
    CancellationToken LockLostToken { get; }
}
