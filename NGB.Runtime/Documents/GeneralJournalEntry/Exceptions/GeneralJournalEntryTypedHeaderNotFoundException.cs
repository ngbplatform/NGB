using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

/// <summary>
/// Thrown when the typed General Journal Entry header row is missing for an existing document.
/// This indicates DB drift/corruption (broken invariant), not a user validation error.
/// </summary>
public sealed class GeneralJournalEntryTypedHeaderNotFoundException(
    string operation,
    Guid documentId)
    : NgbInfrastructureException(
        message: $"General Journal Entry typed header was not found for document '{documentId}' (operation '{operation}').",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
        })
{
    public const string ErrorCodeConst = "gje.header.not_found";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;
}
