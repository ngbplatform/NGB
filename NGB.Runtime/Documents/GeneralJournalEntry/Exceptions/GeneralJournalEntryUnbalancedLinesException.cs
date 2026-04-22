using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryUnbalancedLinesException(
    string operation,
    Guid documentId,
    decimal debit,
    decimal credit)
    : NgbValidationException(
        message: $"Journal entry is not balanced. Debit {debit} vs Credit {credit}.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["debit"] = debit,
            ["credit"] = credit,
        })
{
    public const string ErrorCodeConst = "gje.lines.unbalanced";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public decimal Debit { get; } = debit;

    public decimal Credit { get; } = credit;
}
