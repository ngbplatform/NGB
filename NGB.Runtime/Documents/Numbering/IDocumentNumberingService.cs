using NGB.Core.Documents;

namespace NGB.Runtime.Documents.Numbering;

public interface IDocumentNumberingService
{
    /// <summary>
    /// Ensures a document has a number. If it is already assigned, returns the existing number.
    ///
    /// Requires an active transaction, and assumes the document row is locked (FOR UPDATE) by the caller.
    /// </summary>
    Task<string> EnsureNumberAsync(DocumentRecord documentForUpdate, DateTime nowUtc, CancellationToken ct = default);
}
