using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.UnitOfWork;

/// <summary>
/// Canonical helper for services that support "external transaction" mode.
///
/// Eliminates copy-pasted try/commit/rollback blocks and fails fast on accidental
/// nested transaction usage (which can otherwise lead to committing an outer transaction).
/// </summary>
public static class UnitOfWorkTransactionExtensions
{
    public static Task ExecuteInUowTransactionAsync(
        this IUnitOfWork uow,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
        => uow.ExecuteInUowTransactionAsync(manageTransaction: true, action, ct);

    public static async Task ExecuteInUowTransactionAsync(
        this IUnitOfWork uow,
        bool manageTransaction,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        if (uow is null)
            throw new NgbArgumentRequiredException(nameof(uow));
        
        if (action is null)
            throw new NgbArgumentRequiredException(nameof(action));

        if (manageTransaction)
        {
            if (uow.HasActiveTransaction)
                throw new NgbArgumentInvalidException("manageTransaction", "Transaction already active. manageTransaction=true requires no active transaction. Use manageTransaction=false to run inside an existing transaction.");

            await uow.BeginTransactionAsync(ct);
            try
            {
                await action(ct);
                await uow.CommitAsync(ct);
            }
            catch
            {
                try { await uow.RollbackAsync(ct); }
                catch
                {
                    // ignore: rollback failures must not hide the original exception
                }

                throw;
            }

            return;
        }

        // Keep the canonical message expected by existing tests and callers.
        uow.EnsureActiveTransaction();
        await action(ct);
    }

    public static Task<T> ExecuteInUowTransactionAsync<T>(
        this IUnitOfWork uow,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
        => uow.ExecuteInUowTransactionAsync(manageTransaction: true, action, ct);

    public static async Task<T> ExecuteInUowTransactionAsync<T>(
        this IUnitOfWork uow,
        bool manageTransaction,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (uow is null)
            throw new NgbArgumentRequiredException(nameof(uow));
        
        if (action is null)
            throw new NgbArgumentRequiredException(nameof(action));

        if (manageTransaction)
        {
            if (uow.HasActiveTransaction)
                throw new NgbArgumentInvalidException("manageTransaction", "Transaction already active. manageTransaction=true requires no active transaction. Use manageTransaction=false to run inside an existing transaction.");

            await uow.BeginTransactionAsync(ct);
            try
            {
                var result = await action(ct);
                await uow.CommitAsync(ct);
                return result;
            }
            catch
            {
                try
                {
                    await uow.RollbackAsync(ct);
                }
                catch
                {
                    // ignore: rollback failures must not hide the original exception
                }

                throw;
            }
        }

        // Keep the canonical message expected by existing tests and callers.
        uow.EnsureActiveTransaction();
        return await action(ct);
    }
}
