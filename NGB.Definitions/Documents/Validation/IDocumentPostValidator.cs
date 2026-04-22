using NGB.Core.Documents;

namespace NGB.Definitions.Documents.Validation;

/// <summary>
/// Optional per-document-type validator invoked right before posting.
///
/// The document row is already locked <c>FOR UPDATE</c> and a DB transaction is active.
/// Validator implementations may read typed storage (doc_*) tables and other state.
/// </summary>
public interface IDocumentPostValidator
{
    /// <summary>
    /// Document type code this validator is intended for.
    /// Used only for fail-fast diagnostics.
    /// </summary>
    string TypeCode { get; }

    Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct);
}
