using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

/// <summary>
/// Thrown when allocations processing fails due to a broken internal invariant.
/// This indicates a programmer error or corrupted/invalid internal state.
/// </summary>
public sealed class GeneralJournalEntryAllocationInvariantViolationException(
    string operation,
    Guid documentId,
    string reason)
    : NgbInfrastructureException(
        message: $"Allocation failed due to an invariant violation for document '{documentId}' (operation '{operation}'): {reason}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["reason"] = reason,
        })
{
    public const string ErrorCodeConst = "gje.allocations.invariant_violation";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public string Reason { get; } = reason;
}
