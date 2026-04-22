using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Helper that coordinates per-type typed tables for Draft lifecycle.
/// This is intentionally narrow and does NOT deal with posting/unposting.
///
/// Typed storage hook:
/// - <see cref="IDocumentTypeDraftFullUpdater" /> receives the full <see cref="DocumentRecord" />.
/// </summary>
public sealed class DocumentWriteEngine(
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks,
    IDocumentTypeStorageResolver storageResolver)
{
    public async Task EnsureDraftStorageCreatedAsync(
        Guid documentId,
        string typeCode,
        bool acquireLock,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        if (acquireLock)
            await advisoryLocks.LockDocumentAsync(documentId, ct);

        var storage = storageResolver.TryResolve(typeCode);
        if (storage is null)
            return;

        await storage.CreateDraftAsync(documentId, ct);
    }

    public async Task DeleteDraftStorageAsync(
        Guid documentId,
        string typeCode,
        bool acquireLock,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        if (acquireLock)
            await advisoryLocks.LockDocumentAsync(documentId, ct);

        var storage = storageResolver.TryResolve(typeCode);
        if (storage is null)
            return;

        await storage.DeleteDraftAsync(documentId, ct);
    }

    /// <summary>
    /// Invokes an optional typed storage hook after a Draft document was updated in the common registry.
    /// </summary>
    public async Task UpdateDraftStorageAsync(
        DocumentRecord updatedDraft,
        bool acquireLock,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        if (updatedDraft is null)
            throw new NgbArgumentRequiredException(nameof(updatedDraft));

        if (acquireLock)
            await advisoryLocks.LockDocumentAsync(updatedDraft.Id, ct);

        var storage = storageResolver.TryResolve(updatedDraft.TypeCode);
        if (storage is null)
            return;

        if (storage is IDocumentTypeDraftFullUpdater full)
            await full.UpdateDraftAsync(updatedDraft, ct);
    }
}
