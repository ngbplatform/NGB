using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryAutoReverseOnUtcMustBeAfterDocumentDateException(
    string operation,
    Guid documentId,
    DateOnly documentDayUtc,
    DateOnly autoReverseOnUtc)
    : NgbValidationException(
        message: "Auto reverse date must be after the journal entry date.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["documentDayUtc"] = documentDayUtc.ToString("yyyy-MM-dd"),
            ["autoReverseOnUtc"] = autoReverseOnUtc.ToString("yyyy-MM-dd"),
        })
{
    public const string ErrorCodeConst = "gje.autoreverse.on_utc.after_document_date";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public DateOnly DocumentDayUtc { get; } = documentDayUtc;

    public DateOnly AutoReverseOnUtc { get; } = autoReverseOnUtc;
}
