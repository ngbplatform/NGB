using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Derivations;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Internal;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents.Derivations;

internal sealed class DocumentDerivationService(
    DefinitionsRegistry registry,
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IDocumentRepository documents,
    IDocumentDraftService drafts,
    IDocumentRelationshipService relationships,
    IEnumerable<IDocumentDerivationHandler> handlers) : IDocumentDerivationService
{
    private const string BasedOnCodeNorm = "based_on";
    private readonly IReadOnlyList<IDocumentDerivationHandler> _allHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(handlers);
    private readonly Dictionary<string, IDocumentDerivationHandler> _handlersByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _handlersGate = new();

    public IReadOnlyList<DocumentDerivationAction> ListActionsForSourceType(string sourceTypeCode)
    {
        if (string.IsNullOrWhiteSpace(sourceTypeCode))
            throw new NgbArgumentRequiredException(nameof(sourceTypeCode));

        var code = sourceTypeCode.Trim();

        return registry.DocumentDerivations
            .Where(d => string.Equals(d.FromTypeCode, code, StringComparison.OrdinalIgnoreCase))
            .Select(ToAction)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<DocumentDerivationAction>> ListActionsForDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken ct = default)
    {
        sourceDocumentId.EnsureRequired(nameof(sourceDocumentId));
        var doc = await documents.GetAsync(sourceDocumentId, ct);
        if (doc is null)
            throw new DocumentNotFoundException(sourceDocumentId);

        return ListActionsForSourceType(doc.TypeCode);
    }

    public async Task<Guid> CreateDraftAsync(
        string derivationCode,
        Guid createdFromDocumentId,
        IReadOnlyList<Guid>? basedOnDocumentIds = null,
        DateTime? dateUtc = null,
        string? number = null,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(derivationCode))
            throw new NgbArgumentRequiredException(nameof(derivationCode));

        createdFromDocumentId.EnsureRequired(nameof(createdFromDocumentId));
        var def = ResolveDerivationOrThrow(derivationCode);
        var basedOn = NormalizeBasedOnIds(createdFromDocumentId, basedOnDocumentIds);

        return await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
        {
            // Ensure deterministic locking order across all involved documents to avoid deadlocks.
            await LockDocumentsDeterministicallyAsync(locks, basedOn, innerCt);

            var source = await documents.GetForUpdateAsync(createdFromDocumentId, innerCt);
            if (source is null)
                throw new DocumentNotFoundException(createdFromDocumentId);

            if (!string.Equals(source.TypeCode, def.FromTypeCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new DocumentDerivationSourceTypeMismatchException(
                    derivationCode: def.Code,
                    documentId: createdFromDocumentId,
                    expectedFromTypeCode: def.FromTypeCode,
                    actualFromTypeCode: source.TypeCode);
            }

            var targetDateUtc = dateUtc ?? source.DateUtc;

            // Create the draft (returns document id) inside this transaction.
            var targetId = await drafts.CreateDraftAsync(
                typeCode: def.ToTypeCode,
                number: number,
                dateUtc: targetDateUtc,
                manageTransaction: false,
                ct: innerCt);

            var target = await documents.GetForUpdateAsync(targetId, innerCt)
                ?? throw new DocumentDerivationInvariantViolationException(
                    reason: "draft_missing_after_create",
                    derivedDraftId: targetId);

            var context = new DocumentDerivationContext(def.Code, source, target, basedOn);

            if (def.HandlerType is not null)
            {
                var handler = ResolveHandler(def);

                await handler.ApplyAsync(context, innerCt);

                // Reload header after handler side effects (number/date changes etc.).
                var reloaded = await documents.GetAsync(targetId, innerCt)
                    ?? throw new DocumentDerivationInvariantViolationException(
                        reason: "draft_missing_after_handler",
                        derivedDraftId: targetId);

                context = context with { TargetDraft = reloaded };
            }

            await WriteRelationshipsAsync(relationships, def, targetId, createdFromDocumentId, basedOn, innerCt);

            return targetId;
        }, ct);
    }

    private static DocumentDerivationAction ToAction(DocumentDerivationDefinition def)
        => new(def.Code, def.Name, def.FromTypeCode, def.ToTypeCode, def.RelationshipCodes);

    private DocumentDerivationDefinition ResolveDerivationOrThrow(string derivationCode)
    {
        var code = derivationCode.Trim();

        if (!registry.TryGetDocumentDerivation(code, out var def))
            throw new DocumentDerivationNotFoundException(code);

        return def;
    }

    private static IReadOnlyList<Guid> NormalizeBasedOnIds(
        Guid createdFromDocumentId,
        IReadOnlyList<Guid>? basedOnDocumentIds)
    {
        var set = new HashSet<Guid> { createdFromDocumentId };

        if (basedOnDocumentIds is not null)
        {
            foreach (var id in basedOnDocumentIds)
            {
                if (id == Guid.Empty)
                    continue;

                set.Add(id);
            }
        }

        return set.OrderBy(x => x).ToArray();
    }

    private static async Task LockDocumentsDeterministicallyAsync(
        IAdvisoryLockManager locks,
        IReadOnlyList<Guid> documentIds,
        CancellationToken ct)
    {
        foreach (var id in documentIds.Where(x => x != Guid.Empty).Distinct().OrderBy(x => x))
        {
            await locks.LockDocumentAsync(id, ct);
        }
    }

    private static async Task WriteRelationshipsAsync(
        IDocumentRelationshipService relationships,
        DocumentDerivationDefinition def,
        Guid derivedDraftId,
        Guid createdFromDocumentId,
        IReadOnlyList<Guid> basedOnDocumentIds,
        CancellationToken ct)
    {
        foreach (var relationshipCode in def.RelationshipCodes)
        {
            if (string.IsNullOrWhiteSpace(relationshipCode))
                continue;

            var codeNorm = relationshipCode.Trim().ToLowerInvariant();

            // Convention:
            // - based_on: derived -> each basedOn (incl. createdFrom)
            // - others : derived -> createdFrom
            var targets = codeNorm == BasedOnCodeNorm
                ? basedOnDocumentIds
                : [createdFromDocumentId];

            foreach (var toId in targets)
            {
                if (toId == Guid.Empty)
                    continue;

                await relationships.CreateAsync(
                    fromDocumentId: derivedDraftId,
                    toDocumentId: toId,
                    relationshipCode: relationshipCode,
                    manageTransaction: false,
                    ct: ct);
            }
        }
    }

    private IDocumentDerivationHandler ResolveHandler(DocumentDerivationDefinition def)
    {
        if (_handlersByCode.TryGetValue(def.Code, out var cached))
            return cached;

        lock (_handlersGate)
        {
            if (_handlersByCode.TryGetValue(def.Code, out cached))
                return cached;

            var resolved = BuildHandler(def);
            _handlersByCode[def.Code] = resolved;
            return resolved;
        }
    }

    private IDocumentDerivationHandler BuildHandler(DocumentDerivationDefinition def)
    {
        var handlerType = def.HandlerType
            ?? throw new NgbInvariantViolationException($"Derivation '{def.Code}' has no handler binding.");

        if (!typeof(IDocumentDerivationHandler).IsAssignableFrom(handlerType))
        {
            throw new NgbConfigurationViolationException(
                message: "Document derivation handler must implement IDocumentDerivationHandler.",
                context: new Dictionary<string, object?>
                {
                    ["derivationCode"] = def.Code,
                    ["handlerType"] = handlerType.FullName
                });
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(handlerType, _allHandlers);

        if (matches.Length == 0)
        {
            throw new NgbConfigurationViolationException(
                message: "Document derivation handler is not registered in the DI container.",
                context: new Dictionary<string, object?>
                {
                    ["derivationCode"] = def.Code,
                    ["handlerType"] = handlerType.FullName
                });
        }

        if (matches.Length > 1)
        {
            throw new NgbConfigurationViolationException(
                message: "Multiple document derivation handlers match the configured handler type.",
                context: new Dictionary<string, object?>
                {
                    ["derivationCode"] = def.Code,
                    ["handlerType"] = handlerType.FullName,
                    ["matches"] = matches.Select(handler => handler.GetType().FullName ?? handler.GetType().Name).ToArray()
                });
        }

        return matches[0];
    }
}
