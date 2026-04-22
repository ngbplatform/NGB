using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Posting;

/// <summary>
/// Definitions-backed resolver for optional Reference Register posting handlers.
/// </summary>
public sealed class DefinitionsDocumentReferenceRegisterPostingActionResolver(
    DefinitionsRegistry definitions,
    IEnumerable<IDocumentReferenceRegisterPostingHandler> handlers)
    : IDocumentReferenceRegisterPostingActionResolver
{
    private readonly DefinitionsRegistry _definitions = definitions ?? throw new NgbArgumentRequiredException(nameof(definitions));
    private readonly IReadOnlyList<IDocumentReferenceRegisterPostingHandler> _allHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(
        handlers ?? throw new NgbArgumentRequiredException(nameof(handlers)));
    private readonly Dictionary<string, IDocumentReferenceRegisterPostingHandler> _handlersByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _handlersGate = new();

    public Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation, CancellationToken, Task>? TryResolve(
        DocumentRecord document)
    {
        if (document is null)
            throw new NgbArgumentRequiredException(nameof(document));

        if (!_definitions.TryGetDocument(document.TypeCode, out var def))
            return null;

        if (def.ReferenceRegisterPostingHandlerType is null)
            return null;

        var handler = ResolveHandler(def);
        return (builder, op, ct) => handler.BuildRecordsAsync(document, op, builder, ct);
    }

    private IDocumentReferenceRegisterPostingHandler ResolveHandler(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        if (_handlersByTypeCode.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_handlersGate)
        {
            if (_handlersByTypeCode.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildHandler(def);
            _handlersByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IDocumentReferenceRegisterPostingHandler BuildHandler(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        var postingHandlerType = def.ReferenceRegisterPostingHandlerType
            ?? throw new NgbInvariantViolationException($"Document type '{def.TypeCode}' has no reference register posting handler binding.");
        var postingHandlerTypeName = postingHandlerType.FullName ?? postingHandlerType.Name;

        if (!typeof(IDocumentReferenceRegisterPostingHandler).IsAssignableFrom(postingHandlerType))
        {
            throw new DocumentPostingHandlerMisconfiguredException(
                def.TypeCode,
                postingKind: "reference_register",
                reason: "Posting handler type must implement IDocumentReferenceRegisterPostingHandler.",
                postingHandlerType: postingHandlerTypeName,
                details: new { documentTypeCode = def.TypeCode, postingHandlerType = postingHandlerTypeName });
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(postingHandlerType, _allHandlers);

        if (matches.Length == 0)
        {
            throw new DocumentPostingHandlerMisconfiguredException(
                def.TypeCode,
                postingKind: "reference_register",
                reason: "Posting handler type is not registered in DI container.",
                postingHandlerType: postingHandlerTypeName,
                details: new { documentTypeCode = def.TypeCode, postingHandlerType = postingHandlerTypeName });
        }

        if (matches.Length > 1)
        {
            throw new DocumentPostingHandlerMisconfiguredException(
                def.TypeCode,
                postingKind: "reference_register",
                reason: "Multiple posting handler registrations match the configured posting handler type.",
                postingHandlerType: postingHandlerTypeName,
                details: new
                {
                    documentTypeCode = def.TypeCode,
                    postingHandlerType = postingHandlerTypeName,
                    matches = matches.Select(handler => handler.GetType().FullName ?? handler.GetType().Name).ToArray()
                });
        }

        var resolved = matches[0];
        if (!string.Equals(resolved.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentPostingHandlerMisconfiguredException(
                def.TypeCode,
                postingKind: "reference_register",
                reason: $"Posting handler TypeCode does not match. Expected '{def.TypeCode}', actual '{resolved.TypeCode}'.",
                postingHandlerType: postingHandlerTypeName,
                details: new { documentTypeCode = def.TypeCode, postingHandlerType = postingHandlerTypeName });
        }

        return resolved;
    }
}
