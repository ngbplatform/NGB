using NGB.Core.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Tools.Exceptions;

namespace NGB.Definitions.Documents.Actions;

public sealed class DocumentActionDefinitionsBuilder
{
    private readonly Dictionary<(string DocumentType, string ActionCode), DocumentActionDefinition> _definitions =
        new(DocumentActionDefinitionKeyComparer.Instance);

    public void Add(
        string documentTypeCode,
        DocumentActionMetadata metadata,
        Type? handlerType = null,
        Type? availabilityEvaluatorType = null,
        Type? authorizationEvaluatorType = null,
        string? derivationCode = null)
    {
        if (string.IsNullOrWhiteSpace(documentTypeCode))
            throw new NgbArgumentInvalidException(nameof(documentTypeCode), "Document type code must be non-empty.");

        if (metadata is null)
            throw new NgbArgumentRequiredException(nameof(metadata));

        var documentType = documentTypeCode.Trim();
        ValidateMetadata(metadata);

        var key = (documentType, metadata.Code.Value);
        if (_definitions.ContainsKey(key))
        {
            throw new NgbConfigurationViolationException(
                $"Document action '{metadata.Code}' is already registered for '{documentType}'.",
                new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_action",
                    [StandardDocumentActionCodes.DocumentTypeCode] = documentType,
                    [StandardDocumentActionCodes.DocumentActionCode] = metadata.Code.Value
                });
        }

        _definitions.Add(
            key,
            new DocumentActionDefinition(
                documentType,
                metadata,
                handlerType,
                availabilityEvaluatorType,
                authorizationEvaluatorType,
                string.IsNullOrWhiteSpace(derivationCode) ? null : derivationCode.Trim()));
    }

    public IReadOnlyList<DocumentActionDefinition> Build()
        => _definitions.Values
            .OrderBy(static x => x.DocumentTypeCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Metadata.Order)
            .ThenBy(static x => x.Metadata.Code.Value, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateMetadata(DocumentActionMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Presentation.Label))
            throw new NgbConfigurationViolationException($"Document action '{metadata.Code}' must define a non-empty label.");

        if (metadata.Order < 0)
            throw new NgbConfigurationViolationException($"Document action '{metadata.Code}' order cannot be negative.");

        if (metadata.ExecutionKind is DocumentActionExecutionKind.Navigation or DocumentActionExecutionKind.View
            && metadata.Target is null)
        {
            throw new NgbConfigurationViolationException(
                $"Document action '{metadata.Code}' with execution kind '{metadata.ExecutionKind}' must define a target.");
        }

        if (metadata.Confirmation is { Mode: DocumentActionConfirmationMode.None })
            throw new NgbConfigurationViolationException($"Document action '{metadata.Code}' has a redundant confirmation configuration.");

        if (metadata.Confirmation is not null
            && (string.IsNullOrWhiteSpace(metadata.Confirmation.Title)
                || string.IsNullOrWhiteSpace(metadata.Confirmation.Message)
                || string.IsNullOrWhiteSpace(metadata.Confirmation.ConfirmLabel)))
        {
            throw new NgbConfigurationViolationException(
                $"Document action '{metadata.Code}' confirmation title, message, and confirm label must be non-empty.");
        }
    }

    private sealed class DocumentActionDefinitionKeyComparer
        : IEqualityComparer<(string DocumentType, string ActionCode)>
    {
        public static DocumentActionDefinitionKeyComparer Instance { get; } = new();

        public bool Equals(
            (string DocumentType, string ActionCode) x,
            (string DocumentType, string ActionCode) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.DocumentType, y.DocumentType)
               && StringComparer.OrdinalIgnoreCase.Equals(x.ActionCode, y.ActionCode);

        public int GetHashCode((string DocumentType, string ActionCode) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DocumentType),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ActionCode));
    }
}
