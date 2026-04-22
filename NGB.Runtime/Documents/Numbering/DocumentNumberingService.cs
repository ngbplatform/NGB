using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Numbering;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Numbering;

public sealed class DocumentNumberingService(
    IDocumentRepository documents,
    IDocumentNumberSequenceRepository sequences,
    IDocumentNumberFormatter formatter)
    : IDocumentNumberingService
{
    public async Task<string> EnsureNumberAsync(
        DocumentRecord documentForUpdate,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        nowUtc.EnsureUtc(nameof(nowUtc));

        if (!string.IsNullOrWhiteSpace(documentForUpdate.Number))
            return documentForUpdate.Number!;

        // Numbering is per fiscal year. For now, fiscal year == UTC year of the document date.
        documentForUpdate.DateUtc.EnsureUtc(nameof(documentForUpdate.DateUtc));
        var year = documentForUpdate.DateUtc.Year;

        var next = await sequences.NextAsync(documentForUpdate.TypeCode, year, ct);
        var number = formatter.Format(documentForUpdate.TypeCode, year, next);

        // Idempotency: if another flow already set the number inside the same transaction,
        // TrySetNumberAsync will be a no-op, and we re-read the locked row to return the canonical value.
        var set = await documents.TrySetNumberAsync(documentForUpdate.Id, number, nowUtc, ct);
        if (set)
            return number;

        var reloaded = await documents.GetForUpdateAsync(documentForUpdate.Id, ct)
                       ?? throw new DocumentNotFoundException(documentForUpdate.Id);

        if (string.IsNullOrWhiteSpace(reloaded.Number))
            throw new NgbInvariantViolationException("Document number was not assigned.",
                context: new Dictionary<string, object?> { ["documentId"] = documentForUpdate.Id, ["typeCode"] = documentForUpdate.TypeCode });

        return reloaded.Number!;
    }
}
