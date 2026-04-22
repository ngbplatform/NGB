using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryLinesRequiredException(
    string operation,
    Guid documentId)
    : NgbValidationException(
        message: "At least one line is required.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
        })
{
    public const string ErrorCodeConst = "gje.lines.required";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;
}
