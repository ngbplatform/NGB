using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Core.Documents.Actions;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Tools.Exceptions;
using DocumentActionKind = NGB.Core.Documents.Actions.DocumentActionKind;
using DocumentActionExecutionKind = NGB.Core.Documents.Actions.DocumentActionExecutionKind;
using DocumentActionConfirmationMode = NGB.Core.Documents.Actions.DocumentActionConfirmationMode;

namespace NGB.Runtime.Documents.Actions;

public sealed class DocumentActionRegistry
{
    private readonly IReadOnlyDictionary<(string DocumentType, string ActionCode), DocumentActionDefinition> _byKey;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DocumentActionDefinition>> _byDocumentType;

    public DocumentActionRegistry(
        DefinitionsRegistry definitions,
        IEnumerable<IDocumentActionDefinitionsContributor> contributors)
    {
        var builder = new DocumentActionDefinitionsBuilder();

        foreach (var document in definitions.Documents.OrderBy(static x => x.TypeCode, StringComparer.OrdinalIgnoreCase))
        {
            AddStandardActions(builder, document.TypeCode, HasPostingBehavior(document));
        }

        foreach (var derivation in definitions.DocumentDerivations
            .OrderBy(static x => x.FromTypeCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (!definitions.TryGetDocument(derivation.FromTypeCode, out _)
                || !definitions.TryGetDocument(derivation.ToTypeCode, out _))
            {
                throw new NgbConfigurationViolationException(
                    $"Document derivation '{derivation.Code}' references an unknown document type.");
            }

            builder.Add(
                derivation.FromTypeCode,
                new DocumentActionMetadata(
                    new DocumentActionCode(derivation.Code),
                    new DocumentActionPresentation(derivation.Name, Icon: "file-plus"),
                    DocumentActionKind.Secondary,
                    DocumentActionExecutionKind.Derivation,
                    Order: 500,
                    Target: new DocumentActionTargetMetadata(
                        StandardDocumentTargets.Editor,
                        new Dictionary<string, string?>
                        {
                            [StandardDocumentTargetParameters.DocumentType] = derivation.ToTypeCode,
                            [StandardDocumentTargetParameters.DocumentId] = "{createdDocumentId}"
                        })),
                derivationCode: derivation.Code);
        }

        foreach (var contributor in contributors)
        {
            contributor.Contribute(builder);
        }

        var actions = builder.Build();
        foreach (var action in actions)
        {
            Validate(definitions, action);
        }

        _byKey = actions.ToDictionary(
            static x => (x.DocumentTypeCode, x.Metadata.Code.Value),
            DocumentActionKeyComparer.Instance);

        _byDocumentType = actions
            .GroupBy(static x => x.DocumentTypeCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static x => x.Key,
                static IReadOnlyList<DocumentActionDefinition> (x) => x
                    .OrderBy(static y => y.Metadata.Order)
                    .ThenBy(static y => y.Metadata.Code.Value, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public DocumentActionDefinition Get(string documentType, DocumentActionCode actionCode)
        => _byKey.TryGetValue((documentType, actionCode.Value), out var definition)
            ? definition
            : throw new DocumentActionNotFoundException(documentType, actionCode.Value);

    public IReadOnlyList<DocumentActionDefinition> GetForDocumentType(string documentType)
        => _byDocumentType.TryGetValue(documentType, out var definitions) ? definitions : [];

    private static bool HasPostingBehavior(NGB.Definitions.Documents.DocumentTypeDefinition document)
        => document.PostingHandlerType is not null
           || document.OperationalRegisterPostingHandlerType is not null
           || document.ReferenceRegisterPostingHandlerType is not null;

    private static void AddStandardActions(
        DocumentActionDefinitionsBuilder builder,
        string documentType,
        bool hasPostingBehavior)
    {
        if (hasPostingBehavior)
        {
            builder.Add(
                documentType,
                Command(
                    StandardDocumentActionCodes.Post,
                    "Post",
                    "upload",
                    100,
                    DocumentActionKind.Primary));
            builder.Add(
                documentType,
                Command(
                    StandardDocumentActionCodes.Unpost,
                    "Unpost",
                    "undo",
                    110,
                    DocumentActionKind.Dangerous,
                    Confirm("Unpost document?", "Existing effects will be reversed.", "Unpost")));
            builder.Add(
                documentType,
                Command(
                    StandardDocumentActionCodes.Repost,
                    "Repost",
                    "refresh-cw",
                    120,
                    confirmation: Confirm("Repost document?", "Existing effects will be reversed and rebuilt.", "Repost")));
        }

        builder.Add(
            documentType,
            Command(
                StandardDocumentActionCodes.MarkForDeletion,
                "Mark for deletion",
                "trash-2",
                800,
                DocumentActionKind.Dangerous,
                Confirm("Mark for deletion?", "The draft will be hidden from active work.", "Mark")));
        builder.Add(documentType, Command(StandardDocumentActionCodes.UnmarkForDeletion, "Restore", "rotate-ccw", 810));
        builder.Add(documentType, View(StandardDocumentActionCodes.ViewEffects, "Effects", "document.effects", 900));
        builder.Add(documentType, View(StandardDocumentActionCodes.ViewFlow, "Document flow", "document.flow", 910));
        builder.Add(documentType, View(StandardDocumentActionCodes.ViewAudit, "Audit", "document.audit", 920));
        builder.Add(documentType, View(StandardDocumentActionCodes.Print, "Print", "document.print", 930));
    }

    private static DocumentActionMetadata Command(
        DocumentActionCode code,
        string label,
        string icon,
        int order,
        DocumentActionKind kind = DocumentActionKind.Secondary,
        DocumentActionConfirmationMetadata? confirmation = null)
        => new(
            code,
            new DocumentActionPresentation(label, Icon: icon),
            kind,
            DocumentActionExecutionKind.Command,
            order,
            confirmation);

    private static DocumentActionMetadata View(
        DocumentActionCode code,
        string label,
        string targetCode,
        int order)
        => new(
            code,
            new DocumentActionPresentation(label),
            DocumentActionKind.Secondary,
            DocumentActionExecutionKind.View,
            order,
            Target: new DocumentActionTargetMetadata(
                targetCode,
                new Dictionary<string, string?> { [StandardDocumentTargetParameters.DocumentId] = "{documentId}" }));

    private static DocumentActionConfirmationMetadata Confirm(string title, string message, string confirmLabel)
        => new(DocumentActionConfirmationMode.Confirm, title, message, confirmLabel);

    private static void Validate(DefinitionsRegistry definitions, DocumentActionDefinition action)
    {
        if (!definitions.TryGetDocument(action.DocumentTypeCode, out _))
            throw new NgbConfigurationViolationException($"Document action '{action.Metadata.Code}' references unknown document type '{action.DocumentTypeCode}'.");

        var executable = action.Metadata.ExecutionKind is DocumentActionExecutionKind.Command or DocumentActionExecutionKind.Derivation;
        var standard = IsStandardCommand(action.Metadata.Code);

        if (executable && action.DerivationCode is null && action.HandlerType is null && !standard)
            throw new NgbConfigurationViolationException($"Executable document action '{action.Metadata.Code}' must define a handler or derivation.");

        if (!executable && action.HandlerType is not null)
            throw new NgbConfigurationViolationException($"Navigation/view action '{action.Metadata.Code}' cannot define a command handler.");

        if (action.HandlerType is not null && !typeof(IDocumentActionHandler).IsAssignableFrom(action.HandlerType))
            throw new NgbConfigurationViolationException($"Handler for '{action.Metadata.Code}' must implement {nameof(IDocumentActionHandler)}.");

        if (action.AvailabilityEvaluatorType is not null
            && !typeof(IDocumentActionAvailabilityEvaluator).IsAssignableFrom(action.AvailabilityEvaluatorType))
        {
            throw new NgbConfigurationViolationException(
                $"Availability evaluator for '{action.Metadata.Code}' has an incompatible type.");
        }
        else if (action.AvailabilityEvaluatorType is not null)
        {
            DocumentActionComponentResolver.EnsurePureEvaluator(action.AvailabilityEvaluatorType);
        }

        if (action.AuthorizationEvaluatorType is not null
            && !typeof(IDocumentActionAuthorizationEvaluator).IsAssignableFrom(action.AuthorizationEvaluatorType))
        {
            throw new NgbConfigurationViolationException(
                $"Authorization evaluator for '{action.Metadata.Code}' has an incompatible type.");
        }
        else if (action.AuthorizationEvaluatorType is not null)
        {
            DocumentActionComponentResolver.EnsurePureEvaluator(action.AuthorizationEvaluatorType);
        }
    }

    private static bool IsStandardCommand(DocumentActionCode code)
        => code == StandardDocumentActionCodes.Post
           || code == StandardDocumentActionCodes.Unpost
           || code == StandardDocumentActionCodes.Repost
           || code == StandardDocumentActionCodes.MarkForDeletion
           || code == StandardDocumentActionCodes.UnmarkForDeletion;

    private sealed class DocumentActionKeyComparer : IEqualityComparer<(string DocumentType, string ActionCode)>
    {
        public static DocumentActionKeyComparer Instance { get; } = new();

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
