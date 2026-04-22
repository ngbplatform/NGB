using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryLineDimensionsValidationException(
    string operation,
    Guid documentId,
    int lineNo,
    Guid accountId,
    string accountCode,
    string reason,
    IReadOnlyDictionary<string, object?> details)
    : NgbValidationException(
        message: "Line dimensions are invalid.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["lineNo"] = lineNo,
            ["accountId"] = accountId,
            ["accountCode"] = accountCode,
            ["reason"] = reason,
            ["details"] = details,
        })
{
    public const string ErrorCodeConst = "gje.line.dimensions.invalid";

    public const string ReasonDimensionsNotAllowed = "dimensions_not_allowed";

    public const string ReasonUnknownDimensions = "unknown_dimensions";

    public const string ReasonMissingRequiredDimensions = "missing_required_dimensions";

    public const string ReasonConflictingValues = "conflicting_dimension_values";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public int LineNo { get; } = lineNo;

    public Guid AccountId { get; } = accountId;

    public string AccountCode { get; } = accountCode;

    public string Reason { get; } = reason;

    public IReadOnlyDictionary<string, object?> Details { get; } = details;
}
