using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryAutoReverseOnUtcRequiredException(string operation, Guid documentId)
    : NgbValidationException(
        message: "Auto reverse date is required when Auto reverse is turned on.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
        })
{
    public const string ErrorCodeConst = "gje.autoreverse.on_utc.required";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;
}
