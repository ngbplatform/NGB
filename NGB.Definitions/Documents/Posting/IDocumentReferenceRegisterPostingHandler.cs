using NGB.Core.Documents;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Definitions.Documents.Posting;

/// <summary>
/// Optional document-level posting handler that can build Reference Register records.
///
/// Notes:
/// - Post/Repost/Unpost operations may require different record sets.
/// - Reference Registers are append-only; logical deletion should be represented via tombstone records
///   (<see cref="NGB.ReferenceRegisters.ReferenceRegisterRecordWrite.IsDeleted"/>).
/// </summary>
public interface IDocumentReferenceRegisterPostingHandler
{
    /// <summary>
    /// Document type code this handler is bound to.
    /// Must match <see cref="DocumentRecord.TypeCode"/>.
    /// </summary>
    string TypeCode { get; }

    Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct);
}
