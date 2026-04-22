using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Relationships;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents;

public sealed class DocumentRelationshipService(
    DefinitionsRegistry definitions,
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IDocumentRepository documents,
    IDocumentRelationshipRepository relationships,
    IAuditLogService audit,
    TimeProvider timeProvider)
    : IDocumentRelationshipService
{
    private const int MaxCodeLength = 128;
    private const int CycleGuardMaxDepth = 64;

    public async Task<bool> CreateAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCode,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        ValidateIds(fromDocumentId, toDocumentId);

        var type = ResolveRelationshipTypeOrThrow(definitions, relationshipCode);
        var code = NormalizeCode(type.Code);
        var codeNorm = NormalizeCodeNorm(code);
        var nowUtc = timeProvider.GetUtcNowDateTime();

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await LockBothDocumentsAsync(locks, fromDocumentId, toDocumentId, innerCt);

                var (fromDoc, toDoc) = await EnsureDocumentsExistAndLoadAsync(documents, fromDocumentId, toDocumentId, innerCt);

                EnsureRelationshipAllowed(type, fromDocumentId, toDocumentId, fromDoc.TypeCode, toDoc.TypeCode);
                EnsureDraftRequirements(type, fromDoc, toDoc);

                await EnsureNoCycleAsync(type, relationships, fromDocumentId, toDocumentId, codeNorm, innerCt);

                await EnforceCardinalityAsync(type, relationships, fromDocumentId, toDocumentId, codeNorm, innerCt);

                var createdAny = false;

                createdAny |= await TryCreateOneAsync(
                    relationships,
                    audit,
                    DeterministicDocumentRelationshipId.FromNormalizedCode(fromDocumentId, codeNorm, toDocumentId),
                    fromDocumentId,
                    toDocumentId,
                    code,
                    codeNorm,
                    nowUtc,
                    innerCt);

                if (type.IsBidirectional)
                {
                    // For bidirectional relationship types, a 2-cycle is inherent (A -> B and B -> A).
                    // Therefore cycle guards are applied only to directed relationships.
                    await EnforceCardinalityAsync(type, relationships, toDocumentId, fromDocumentId, codeNorm, innerCt);

                    createdAny |= await TryCreateOneAsync(
                        relationships,
                        audit,
                        DeterministicDocumentRelationshipId.FromNormalizedCode(toDocumentId, codeNorm, fromDocumentId),
                        toDocumentId,
                        fromDocumentId,
                        code,
                        codeNorm,
                        nowUtc,
                        innerCt);
                }

                return createdAny;
            },
            ct);
    }

    public async Task<bool> DeleteAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCode,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        ValidateIds(fromDocumentId, toDocumentId);

        var type = ResolveRelationshipTypeOrThrow(definitions, relationshipCode);
        var code = NormalizeCode(type.Code);
        var codeNorm = NormalizeCodeNorm(code);

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await LockBothDocumentsAsync(locks, fromDocumentId, toDocumentId, innerCt);

                var (fromDoc, toDoc) = await EnsureDocumentsExistAndLoadAsync(documents, fromDocumentId, toDocumentId, innerCt);

                EnsureRelationshipAllowed(type, fromDocumentId, toDocumentId, fromDoc.TypeCode, toDoc.TypeCode);
                EnsureDraftRequirements(type, fromDoc, toDoc);

                var deletedAny = false;

                deletedAny |= await TryDeleteOneAsync(
                    relationships,
                    audit,
                    DeterministicDocumentRelationshipId.FromNormalizedCode(fromDocumentId, codeNorm, toDocumentId),
                    innerCt);

                if (type.IsBidirectional)
                {
                    deletedAny |= await TryDeleteOneAsync(
                        relationships,
                        audit,
                        DeterministicDocumentRelationshipId.FromNormalizedCode(toDocumentId, codeNorm, fromDocumentId),
                        innerCt);
                }

                return deletedAny;
            },
            ct);
    }

    public Task<IReadOnlyList<DocumentRelationshipRecord>> ListOutgoingAsync(
        Guid fromDocumentId,
        CancellationToken ct = default)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        return relationships.ListOutgoingAsync(fromDocumentId, ct);
    }

    public Task<IReadOnlyList<DocumentRelationshipRecord>> ListIncomingAsync(
        Guid toDocumentId,
        CancellationToken ct = default)
    {
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        return relationships.ListIncomingAsync(toDocumentId, ct);
    }

    private static async Task<bool> TryCreateOneAsync(
        IDocumentRelationshipRepository relationships,
        IAuditLogService audit,
        Guid relationshipId,
        Guid fromDocumentId,
        Guid toDocumentId,
        string code,
        string codeNorm,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var record = new DocumentRelationshipRecord
        {
            Id = relationshipId,
            FromDocumentId = fromDocumentId,
            ToDocumentId = toDocumentId,
            RelationshipCode = code,
            RelationshipCodeNorm = codeNorm,
            CreatedAtUtc = nowUtc
        };

        var created = await relationships.TryCreateAsync(record, ct);
        if (!created)
            return false; // idempotent no-op

        await audit.WriteAsync(
            entityKind: AuditEntityKind.DocumentRelationship,
            entityId: relationshipId,
            actionCode: AuditActionCodes.DocumentRelationshipCreate,
            changes:
            [
                AuditLogService.Change("from_document_id", null, fromDocumentId),
                AuditLogService.Change("to_document_id", null, toDocumentId),
                AuditLogService.Change("relationship_code", null, code),
                AuditLogService.Change("relationship_code_norm", null, codeNorm)
            ],
            metadata: new { fromDocumentId, toDocumentId, relationshipCode = code },
            ct: ct);

        return true;
    }

    private static async Task<bool> TryDeleteOneAsync(
        IDocumentRelationshipRepository relationships,
        IAuditLogService audit,
        Guid relationshipId,
        CancellationToken ct)
    {
        var existing = await relationships.GetAsync(relationshipId, ct);
        if (existing is null)
            return false; // idempotent no-op

        var deleted = await relationships.TryDeleteAsync(relationshipId, ct);
        if (!deleted)
            return false;

        await audit.WriteAsync(
            entityKind: AuditEntityKind.DocumentRelationship,
            entityId: relationshipId,
            actionCode: AuditActionCodes.DocumentRelationshipDelete,
            changes:
            [
                AuditLogService.Change("from_document_id", existing.FromDocumentId, null),
                AuditLogService.Change("to_document_id", existing.ToDocumentId, null),
                AuditLogService.Change("relationship_code", existing.RelationshipCode, null),
                AuditLogService.Change("relationship_code_norm", existing.RelationshipCodeNorm, null)
            ],
            metadata: new
            {
                fromDocumentId = existing.FromDocumentId,
                toDocumentId = existing.ToDocumentId,
                relationshipCode = existing.RelationshipCode
            },
            ct: ct);

        return true;
    }

    private static DocumentRelationshipTypeDefinition ResolveRelationshipTypeOrThrow(DefinitionsRegistry definitions, string relationshipCode)
    {
        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentRequiredException(nameof(relationshipCode));

        var candidate = relationshipCode.Trim();
        if (!definitions.TryGetDocumentRelationshipType(candidate, out var type))
            throw new DocumentRelationshipTypeNotFoundException(candidate);

        return type;
    }

    private static void EnsureRelationshipAllowed(
        DocumentRelationshipTypeDefinition type,
        Guid fromDocumentId,
        Guid toDocumentId,
        string fromTypeCode,
        string toTypeCode)
    {
        if (type.AllowedFromTypeCodes is not null
            && !type.AllowedFromTypeCodes.Contains(fromTypeCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new DocumentRelationshipValidationException(
                reason: "not_allowed_from_type",
                relationshipCode: type.Code,
                fromDocumentId: fromDocumentId,
                toDocumentId: toDocumentId,
                extraContext: new Dictionary<string, object?>
                {
                    ["fromTypeCode"] = fromTypeCode,
                    ["toTypeCode"] = toTypeCode
                });
        }

        if (type.AllowedToTypeCodes is not null
            && !type.AllowedToTypeCodes.Contains(toTypeCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new DocumentRelationshipValidationException(
                reason: "not_allowed_to_type",
                relationshipCode: type.Code,
                fromDocumentId: fromDocumentId,
                toDocumentId: toDocumentId,
                extraContext: new Dictionary<string, object?>
                {
                    ["fromTypeCode"] = fromTypeCode,
                    ["toTypeCode"] = toTypeCode
                });
        }

        if (type.IsBidirectional)
        {
            // Reverse direction must also be valid.
            if (type.AllowedFromTypeCodes is not null
                && !type.AllowedFromTypeCodes.Contains(toTypeCode, StringComparer.OrdinalIgnoreCase))
            {
                throw new DocumentRelationshipValidationException(
                    reason: "bidirectional_reverse_not_allowed_from_type",
                    relationshipCode: type.Code,
                    fromDocumentId: fromDocumentId,
                    toDocumentId: toDocumentId,
                    extraContext: new Dictionary<string, object?>
                    {
                        ["fromTypeCode"] = fromTypeCode,
                        ["toTypeCode"] = toTypeCode
                    });
            }

            if (type.AllowedToTypeCodes is not null
                && !type.AllowedToTypeCodes.Contains(fromTypeCode, StringComparer.OrdinalIgnoreCase))
            {
                throw new DocumentRelationshipValidationException(
                    reason: "bidirectional_reverse_not_allowed_to_type",
                    relationshipCode: type.Code,
                    fromDocumentId: fromDocumentId,
                    toDocumentId: toDocumentId,
                    extraContext: new Dictionary<string, object?>
                    {
                        ["fromTypeCode"] = fromTypeCode,
                        ["toTypeCode"] = toTypeCode
                    });
            }
        }
    }

    private static void EnsureDraftRequirements(
        DocumentRelationshipTypeDefinition type,
        DocumentRecord fromDoc,
        DocumentRecord toDoc)
    {
        if (fromDoc.Status != DocumentStatus.Draft)
            throw new DocumentRelationshipValidationException(
                reason: "from_document_must_be_draft",
                relationshipCode: type.Code,
                fromDocumentId: fromDoc.Id,
                toDocumentId: toDoc.Id,
                extraContext: new Dictionary<string, object?>
                {
                    ["fromStatus"] = fromDoc.Status.ToString(),
                    ["toStatus"] = toDoc.Status.ToString()
                });

        if (type.IsBidirectional && toDoc.Status != DocumentStatus.Draft)
            throw new DocumentRelationshipValidationException(
                reason: "bidirectional_requires_both_draft",
                relationshipCode: type.Code,
                fromDocumentId: fromDoc.Id,
                toDocumentId: toDoc.Id,
                extraContext: new Dictionary<string, object?>
                {
                    ["fromStatus"] = fromDoc.Status.ToString(),
                    ["toStatus"] = toDoc.Status.ToString()
                });
    }

    private static async Task EnforceCardinalityAsync(
        DocumentRelationshipTypeDefinition type,
        IDocumentRelationshipRepository relationships,
        Guid fromDocumentId,
        Guid toDocumentId,
        string codeNorm,
        CancellationToken ct)
    {
        if (type.MaxOutgoingPerFrom == 1)
        {
            var existingOutgoing = await relationships.GetSingleOutgoingByCodeNormAsync(fromDocumentId, codeNorm, ct);
            if (existingOutgoing is not null && existingOutgoing.ToDocumentId != toDocumentId)
            {
                throw new DocumentRelationshipValidationException(
                    reason: "cardinality_max_outgoing_per_from",
                    relationshipCode: type.Code,
                    fromDocumentId: fromDocumentId,
                    toDocumentId: toDocumentId,
                    extraContext: new Dictionary<string, object?>
                    {
                        ["existingToDocumentId"] = existingOutgoing.ToDocumentId
                    });
            }
        }

        if (type.MaxIncomingPerTo == 1)
        {
            var existingIncoming = await relationships.GetSingleIncomingByCodeNormAsync(toDocumentId, codeNorm, ct);
            if (existingIncoming is not null && existingIncoming.FromDocumentId != fromDocumentId)
            {
                throw new DocumentRelationshipValidationException(
                    reason: "cardinality_max_incoming_per_to",
                    relationshipCode: type.Code,
                    fromDocumentId: fromDocumentId,
                    toDocumentId: toDocumentId,
                    extraContext: new Dictionary<string, object?>
                    {
                        ["existingFromDocumentId"] = existingIncoming.FromDocumentId
                    });
            }
        }
    }

    private static async Task EnsureNoCycleAsync(
        DocumentRelationshipTypeDefinition type,
        IDocumentRelationshipRepository relationships,
        Guid fromDocumentId,
        Guid toDocumentId,
        string codeNorm,
        CancellationToken ct)
    {
        // Cycles are expected/allowed for bidirectional relationship types.
        if (type.IsBidirectional)
            return;

        // Adding edge (from -> to) creates a cycle iff a path already exists (to -> from).
        var createsCycle = await relationships.ExistsPathAsync(
            fromDocumentId: toDocumentId,
            toDocumentId: fromDocumentId,
            relationshipCodeNorm: codeNorm,
            maxDepth: CycleGuardMaxDepth,
            ct);

        if (createsCycle)
            throw new DocumentRelationshipValidationException(
                reason: "cycle_detected",
                relationshipCode: type.Code,
                fromDocumentId: fromDocumentId,
                toDocumentId: toDocumentId);
    }

    private static void ValidateIds(Guid fromDocumentId, Guid toDocumentId)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        
        if (fromDocumentId == toDocumentId)
            throw new NgbArgumentInvalidException(nameof(fromDocumentId), "fromDocumentId and toDocumentId must be different.");
    }

    private static string NormalizeCode(string relationshipCode)
    {
        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentRequiredException(nameof(relationshipCode));

        var code = relationshipCode.Trim();
        if (code.Length > MaxCodeLength)
            throw new NgbArgumentInvalidException(nameof(relationshipCode), $"relationshipCode exceeds max length {MaxCodeLength}.");

        return code;
    }

    private static string NormalizeCodeNorm(string code) => code.ToLowerInvariant();

    private static async Task LockBothDocumentsAsync(IAdvisoryLockManager locks, Guid a, Guid b, CancellationToken ct)
    {
        // Always take locks in a deterministic order to avoid deadlocks.
        if (a.CompareTo(b) <= 0)
        {
            await locks.LockDocumentAsync(a, ct);
            await locks.LockDocumentAsync(b, ct);
        }
        else
        {
            await locks.LockDocumentAsync(b, ct);
            await locks.LockDocumentAsync(a, ct);
        }
    }

    private static async Task<(DocumentRecord From, DocumentRecord To)> EnsureDocumentsExistAndLoadAsync(
        IDocumentRepository documents,
        Guid fromDocumentId,
        Guid toDocumentId,
        CancellationToken ct)
    {
        // Row-level locks reinforce the invariant at the SQL level and provide a stable view within the txn.
        // Lock in deterministic order to avoid deadlocks in tests with parallel writers.
        var first = fromDocumentId.CompareTo(toDocumentId) <= 0 ? fromDocumentId : toDocumentId;
        var second = first == fromDocumentId ? toDocumentId : fromDocumentId;

        var firstDoc = await documents.GetForUpdateAsync(first, ct);
        if (firstDoc is null)
            throw new DocumentNotFoundException(first);

        var secondDoc = await documents.GetForUpdateAsync(second, ct);
        if (secondDoc is null)
            throw new DocumentNotFoundException(second);

        var fromDoc = fromDocumentId == first ? firstDoc : secondDoc;
        var toDoc = toDocumentId == first ? firstDoc : secondDoc;

        return (fromDoc, toDoc);
    }
}
