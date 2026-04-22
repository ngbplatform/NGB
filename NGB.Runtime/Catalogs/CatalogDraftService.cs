using NGB.Core.AuditLog;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Catalogs;

/// <summary>
/// Draft-like lifecycle for catalog registry rows.
///
/// For now, catalogs have a minimal lifecycle (Create, MarkForDeletion/UnmarkForDeletion).
///
/// Important: catalogs use *strict soft delete* (catalogs.is_deleted).
/// Typed storage rows (cat_* tables) are NOT physically deleted on mark/unmark.
/// Per-type storage (cat_* tables) is coordinated by CatalogWriteEngine.
/// </summary>
public sealed class CatalogDraftService(
    IUnitOfWork uow,
    ICatalogRepository repo,
    CatalogWriteEngine writeEngine,
    ICatalogTypeRegistry catalogTypes,
    IAuditLogService audit,
    TimeProvider timeProvider)
    : ICatalogDraftService
{
    public Task<Guid> CreateAsync(string catalogCode, bool manageTransaction, CancellationToken ct)
        => CreateAsync(catalogCode, manageTransaction, suppressAudit: false, ct);

    public async Task<Guid> CreateAsync(
        string catalogCode,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(catalogCode))
            throw new NgbArgumentRequiredException(nameof(catalogCode));

        if (!catalogTypes.TryGet(catalogCode, out _))
            throw new CatalogTypeNotFoundException(catalogCode);

        var id = Guid.CreateVersion7();
        var nowUtc = timeProvider.GetUtcNowDateTime();

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await CreateInCurrentTransactionAsync(
                    id,
                    catalogCode,
                    nowUtc,
                    ensureTypedStorage: true,
                    suppressAudit: suppressAudit,
                    ct: innerCt);
                
                return id;
            },
            ct);
    }

    public Task<Guid> CreateHeaderOnlyAsync(string catalogCode, bool manageTransaction, CancellationToken ct)
        => CreateHeaderOnlyAsync(catalogCode, manageTransaction, suppressAudit: false, ct);

    public async Task<Guid> CreateHeaderOnlyAsync(
        string catalogCode,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(catalogCode))
            throw new NgbArgumentRequiredException(nameof(catalogCode));

        if (!catalogTypes.TryGet(catalogCode, out _))
            throw new CatalogTypeNotFoundException(catalogCode);

        var id = Guid.CreateVersion7();
        var nowUtc = timeProvider.GetUtcNowDateTime();

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await CreateInCurrentTransactionAsync(
                    id,
                    catalogCode,
                    nowUtc,
                    ensureTypedStorage: false,
                    suppressAudit: suppressAudit,
                    ct: innerCt);

                return id;
            },
            ct);
    }

    public async Task MarkForDeletionAsync(Guid catalogId, bool manageTransaction = true, CancellationToken ct = default)
    {
        catalogId.EnsureRequired(nameof(catalogId));
        await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                var (isNoOp, catalogCode) = await MarkDeletedInCurrentTransactionAsync(catalogId, innerCt);
                if (isNoOp)
                    return;

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Catalog,
                    entityId: catalogId,
                    actionCode: AuditActionCodes.CatalogMarkForDeletion,
                    changes: [AuditLogService.Change("is_deleted", false, true)],
                    metadata: new { catalogCode },
                    ct: innerCt);
            },
            ct);
    }

    public async Task UnmarkForDeletionAsync(Guid catalogId, bool manageTransaction = true, CancellationToken ct = default)
    {
        catalogId.EnsureRequired(nameof(catalogId));
        await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                var (isNoOp, catalogCode) = await UnmarkDeletedInCurrentTransactionAsync(catalogId, innerCt);
                if (isNoOp)
                    return;

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Catalog,
                    entityId: catalogId,
                    actionCode: AuditActionCodes.CatalogUnmarkForDeletion,
                    changes: [AuditLogService.Change("is_deleted", true, false)],
                    metadata: new { catalogCode },
                    ct: innerCt);
            },
            ct);
    }

    private async Task CreateInCurrentTransactionAsync(
        Guid id,
        string catalogCode,
        DateTime nowUtc,
        bool ensureTypedStorage,
        bool suppressAudit,
        CancellationToken ct)
    {
        var record = new CatalogRecord
        {
            Id = id,
            CatalogCode = catalogCode,
            IsDeleted = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        await repo.CreateAsync(record, ct);

        if (ensureTypedStorage)
            await writeEngine.EnsureStorageCreatedAsync(id, catalogCode, ct);

        if (!suppressAudit)
        {
            await audit.WriteAsync(
                entityKind: AuditEntityKind.Catalog,
                entityId: id,
                actionCode: AuditActionCodes.CatalogCreate,
                changes:
                [
                    AuditLogService.Change("catalog_code", null, catalogCode),
                    AuditLogService.Change("is_deleted", null, false)
                ],
                metadata: new { catalogCode },
                ct: ct);
        }
    }

    /// <summary>
    /// Returns (IsNoOp, CatalogCode). IsNoOp is true if the operation completed as a no-op (already deleted).
    /// </summary>
    private async Task<(bool IsNoOp, string CatalogCode)> MarkDeletedInCurrentTransactionAsync(
        Guid catalogId,
        CancellationToken ct)
    {
        var locked = await repo.GetForUpdateAsync(catalogId, ct);
        if (locked is null)
            throw new CatalogNotFoundException(catalogId);

        if (locked.IsDeleted)
            return (true, locked.CatalogCode); // idempotent

        await repo.MarkForDeletionAsync(catalogId, timeProvider.GetUtcNowDateTime(), ct);

        return (false, locked.CatalogCode);
    }

    /// <summary>
    /// Returns (IsNoOp, CatalogCode). IsNoOp is true if the operation completed as a no-op (already not deleted).
    /// </summary>
    private async Task<(bool IsNoOp, string CatalogCode)> UnmarkDeletedInCurrentTransactionAsync(
        Guid catalogId,
        CancellationToken ct)
    {
        var locked = await repo.GetForUpdateAsync(catalogId, ct);
        if (locked is null)
            throw new CatalogNotFoundException(catalogId);

        if (!locked.IsDeleted)
            return (true, locked.CatalogCode); // idempotent

        await repo.UnmarkForDeletionAsync(catalogId, timeProvider.GetUtcNowDateTime(), ct);

        return (false, locked.CatalogCode);
    }
}
