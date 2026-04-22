using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryDebitAndCreditLinesRequiredException(
    string operation,
    Guid documentId)
    : NgbValidationException(
        message: "Both debit and credit sides must have at least one line.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
        })
{
    public const string ErrorCodeConst = "gje.lines.debit_and_credit_required";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;
}
