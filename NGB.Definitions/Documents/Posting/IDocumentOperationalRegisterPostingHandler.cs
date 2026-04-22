using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Definitions.Documents.Posting;

/// <summary>
/// Optional document-level posting handler that can build Operational Register movements.
///
/// Notes:
/// - Movements are payload for Post/Repost operations.
/// - Unpost is handled by Operational Registers as storno append (no deletes) and does not require a payload.
/// </summary>
public interface IDocumentOperationalRegisterPostingHandler
{
    /// <summary>
    /// Document type code this handler is bound to.
    /// Must match <see cref="DocumentRecord.TypeCode"/>.
    /// </summary>
    string TypeCode { get; }

    Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct);
}
