using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.UnitOfWork;

/// <summary>
/// Helpers to keep Postgres repositories consistent:
/// any operation that requires an active <see cref="IUnitOfWork"/> transaction should also
/// ensure the underlying connection is open.
/// </summary>
internal static class UnitOfWorkTransactionExtensions
{
    public static async Task EnsureOpenForTransactionAsync(this IUnitOfWork uow, CancellationToken ct = default)
    {
        if (uow is null)
            throw new NgbArgumentRequiredException(nameof(uow));

        // Fail fast: transactional atomicity is required.
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);
    }
}
