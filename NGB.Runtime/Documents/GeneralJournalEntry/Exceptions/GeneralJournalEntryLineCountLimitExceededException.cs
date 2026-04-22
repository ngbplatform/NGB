using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryLineCountLimitExceededException(
    string operation,
    Guid documentId,
    int attemptedCount,
    int maxAllowed)
    : NgbValidationException(
        message: $"A single GJE cannot exceed {maxAllowed} lines.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["attemptedCount"] = attemptedCount,
            ["maxAllowed"] = maxAllowed,
        })
{
    public const string ErrorCodeConst = "gje.lines.limit_exceeded";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public int AttemptedCount { get; } = attemptedCount;

    public int MaxAllowed { get; } = maxAllowed;
}
