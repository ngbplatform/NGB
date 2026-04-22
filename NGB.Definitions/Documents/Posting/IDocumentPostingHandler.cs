using NGB.Accounting.Posting;
using NGB.Core.Documents;

namespace NGB.Definitions.Documents.Posting;

/// <summary>
/// A per-document-type strategy responsible for generating accounting entries.
///
/// IMPORTANT:
/// Runtime executes posting inside the same DB transaction as document status changes.
/// Therefore posting handlers must be deterministic and idempotent.
/// </summary>
public interface IDocumentPostingHandler
{
    /// <summary>
    /// Document type code this handler is intended for.
    /// Used only for fail-fast diagnostics.
    /// </summary>
    string TypeCode { get; }

    Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct);
}
