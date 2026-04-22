using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryLineAmountMustBePositiveException(
    string operation,
    Guid documentId,
    int lineNo,
    decimal amount)
    : NgbValidationException(
        message: $"Line {lineNo} amount must be greater than 0.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["lineNo"] = lineNo,
            ["amount"] = amount,
        })
{
    public const string ErrorCodeConst = "gje.line.amount_must_be_positive";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public int LineNo { get; } = lineNo;

    public decimal Amount { get; } = amount;
}
