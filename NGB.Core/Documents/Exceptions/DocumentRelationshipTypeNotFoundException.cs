using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentRelationshipTypeNotFoundException(string relationshipCode) : NgbConfigurationException(
    message: $"Document relationship type '{relationshipCode}' was not found. Register it via Definitions.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["relationshipCode"] = relationshipCode
    })
{
    public const string Code = "doc.relationship_type.not_found";

    public string RelationshipCode { get; } = relationshipCode;
}
