using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryBusinessFieldRequiredException(
    string operation,
    Guid documentId,
    string fieldName)
    : NgbValidationException(
        message: $"{GetLabel(fieldName)} is required.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["field"] = fieldName,
            ["fieldLabel"] = GetLabel(fieldName),
        })
{
    public const string ErrorCodeConst = "gje.business_field.required";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public string FieldName { get; } = fieldName;

    private static string GetLabel(string fieldName) => fieldName switch
    {
        "ReasonCode" => "Reason code",
        "Memo" => "Memo",
        _ => NgbArgumentLabelFormatter.Format(fieldName)
    };
}
