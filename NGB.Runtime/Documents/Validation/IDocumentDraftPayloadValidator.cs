using NGB.Contracts.Common;

namespace NGB.Runtime.Documents.Validation;

/// <summary>
/// Optional per-document-type validator invoked during draft create/update.
///
/// This validator can enforce invariants that depend on the incoming payload
/// (fields/parts) before the draft is persisted.
///
/// NOTE: This interface lives in Runtime because it depends on the HTTP contract DTOs.
/// Definitions (platform boundary) MUST NOT depend on NGB.Contracts.
/// </summary>
public interface IDocumentDraftPayloadValidator
{
    /// <summary>
    /// Document type code this validator is intended for.
    /// Used only for filtering and fail-fast diagnostics.
    /// </summary>
    string TypeCode { get; }

    Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct);

    Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct);
}
