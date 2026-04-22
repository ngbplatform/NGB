using NGB.Core.Documents;

namespace NGB.Definitions.Documents.Validation;

/// <summary>
/// Optional per-document-type validator invoked during draft creation.
/// This validator can enforce platform or module invariants that depend only on the common document registry fields.
/// </summary>
public interface IDocumentDraftValidator
{
    /// <summary>
    /// Document type code this validator is intended for.
    /// Used only for fail-fast diagnostics.
    /// </summary>
    string TypeCode { get; }

    Task ValidateCreateDraftAsync(DocumentRecord draft, CancellationToken ct);
}
