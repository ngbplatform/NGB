using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Numbering;

public sealed class DocumentNumberingAndTypedSyncService(
    IDocumentNumberingService numbering,
    DocumentWriteEngine writeEngine)
    : IDocumentNumberingAndTypedSyncService
{
    public async Task<string> EnsureNumberAndSyncTypedAsync(
        DocumentRecord documentForUpdate,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (documentForUpdate is null)
            throw new NgbArgumentRequiredException(nameof(documentForUpdate));

        // Most numbering policies only assign a number if it is missing.
        // We rely on that contract and only synchronize typed storage when a number was newly assigned.
        var hadNumber = !string.IsNullOrWhiteSpace(documentForUpdate.Number);

        var assigned = await numbering.EnsureNumberAsync(documentForUpdate, nowUtc, ct);

        if (!hadNumber && !string.IsNullOrWhiteSpace(assigned))
        {
            // DocumentRecord is immutable (init-only). Numbering updates the DB and returns the assigned number,
            // but the caller may still hold a pre-number snapshot.
            // Synchronize typed storage with a record that definitely includes the assigned number.
            var forTypedSync = string.IsNullOrWhiteSpace(documentForUpdate.Number)
                ? new DocumentRecord
                {
                    Id = documentForUpdate.Id,
                    TypeCode = documentForUpdate.TypeCode,
                    Number = assigned,
                    DateUtc = documentForUpdate.DateUtc,
                    Status = documentForUpdate.Status,
                    CreatedAtUtc = documentForUpdate.CreatedAtUtc,
                    UpdatedAtUtc = documentForUpdate.UpdatedAtUtc,
                    PostedAtUtc = documentForUpdate.PostedAtUtc,
                    MarkedForDeletionAtUtc = documentForUpdate.MarkedForDeletionAtUtc,
                }
                : documentForUpdate;

            await writeEngine.UpdateDraftStorageAsync(forTypedSync, acquireLock: false, ct);
        }

        return assigned;
    }
}
