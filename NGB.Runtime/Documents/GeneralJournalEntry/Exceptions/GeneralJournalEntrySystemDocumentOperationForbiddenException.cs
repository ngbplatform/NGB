using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntrySystemDocumentOperationForbiddenException(
    string operation,
    Guid documentId)
    : NgbForbiddenException(
        message: "Operation is forbidden for system journal entries.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
        })
{
    public const string ErrorCodeConst = "gje.system_document.operation_forbidden";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;
}
