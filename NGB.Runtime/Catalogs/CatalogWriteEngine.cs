using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Catalogs;

/// <summary>
/// Narrow engine that coordinates per-type catalog storage (cat_* tables).
///
/// IMPORTANT:
/// - Requires an active transaction.
/// - Does NOT manage lifecycle in 'catalogs' table (handled by CatalogDraftService via ICatalogRepository).
/// - Does NOT know anything about accounting/posting.
/// </summary>
public sealed class CatalogWriteEngine(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    ICatalogTypeStorageResolver storageResolver)
{
    public async Task EnsureStorageCreatedAsync(Guid catalogId, string catalogCode, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        await locks.LockCatalogAsync(catalogId, ct);

        var storage = storageResolver.TryResolve(catalogCode);
        if (storage is null)
            return; // no per-type tables for this catalog code

        try
        {
            await storage.EnsureCreatedAsync(catalogId, ct);
        }
        catch (NgbException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CatalogTypedStorageOperationException(
                catalogId: catalogId,
                catalogCode: catalogCode,
                operation: "ensure_created",
                details: new { exception = ex.GetType().Name, message = ex.Message },
                innerException: ex);
        }
    }

    public async Task DeleteStorageAsync(Guid catalogId, string catalogCode, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        await locks.LockCatalogAsync(catalogId, ct);

        var storage = storageResolver.TryResolve(catalogCode);
        if (storage is null)
            return;

        try
        {
            await storage.DeleteAsync(catalogId, ct);
        }
        catch (NgbException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CatalogTypedStorageOperationException(
                catalogId: catalogId,
                catalogCode: catalogCode,
                operation: "delete",
                details: new { exception = ex.GetType().Name, message = ex.Message },
                innerException: ex);
        }
    }
}
