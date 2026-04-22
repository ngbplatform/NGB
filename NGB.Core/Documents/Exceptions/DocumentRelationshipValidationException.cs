using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentRelationshipValidationException(
    string reason,
    string relationshipCode,
    Guid fromDocumentId,
    Guid toDocumentId,
    IReadOnlyDictionary<string, object?>? extraContext = null)
    : NgbValidationException(message:
        $"Document relationship operation is invalid ({reason}). relationship='{relationshipCode}', from_document_id={fromDocumentId}, to_document_id={toDocumentId}.",
        errorCode: Code,
        context: BuildContext(reason, relationshipCode, fromDocumentId, toDocumentId, extraContext))
{
    public const string Code = "doc.relationship.validation_failed";

    public string Reason { get; } = reason;
    public string RelationshipCode { get; } = relationshipCode;
    public Guid FromDocumentId { get; } = fromDocumentId;
    public Guid ToDocumentId { get; } = toDocumentId;

    private static Dictionary<string, object?> BuildContext(
        string reason,
        string relationshipCode,
        Guid fromDocumentId,
        Guid toDocumentId,
        IReadOnlyDictionary<string, object?>? extraContext)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["relationshipCode"] = relationshipCode,
            ["fromDocumentId"] = fromDocumentId,
            ["toDocumentId"] = toDocumentId
        };

        if (extraContext is not null)
        {
            foreach (var (k, v) in extraContext)
                ctx[k] = v;
        }

        return ctx;
    }
}
