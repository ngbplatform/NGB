namespace NGB.Core.Documents.Actions;

/// <summary>
/// Stable keys for the in-process document-action evaluation context.
/// These are domain evaluation facts, not transport or UI-navigation parameters.
/// </summary>
public static class DocumentActionContextKeys
{
    public const string DocumentType = "documentType";
    public const string DocumentTypeCode = "documentTypeCode";
    public const string DocumentId = "documentId";
    public const string ActionCode = "actionCode";
}
