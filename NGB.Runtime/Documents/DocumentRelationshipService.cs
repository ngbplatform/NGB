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
    : IDocumentRelationshipBatchService
{
    private const int MaxCodeLength = 128;
    private const int CycleGuardMaxDepth = 64;
    private const int MaxBatchSize = 1_000;

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

    public Task<int> CreateManyAsync(
        IReadOnlyCollection<DocumentRelationshipCreateRequest> requests,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (requests is null)
            throw new NgbArgumentRequiredException(nameof(requests));

        if (requests.Count == 0)
            return Task.FromResult(0);

        if (requests.Count > MaxBatchSize)
            throw new NgbArgumentOutOfRangeException(nameof(requests), requests.Count, $"At most {MaxBatchSize} relationships are allowed per batch.");

        var items = requests
            .Select(request =>
            {
                if (request is null)
                    throw new NgbArgumentInvalidException(nameof(requests), "Relationship batch must not contain null items.");

                ValidateIds(request.FromDocumentId, request.ToDocumentId);
                var type = ResolveRelationshipTypeOrThrow(definitions, request.RelationshipCode);
                var code = NormalizeCode(type.Code);
                return new BatchItem(
                    request.FromDocumentId,
                    request.ToDocumentId,
                    type,
                    code,
                    NormalizeCodeNorm(code));
            })
            .DistinctBy(static item => (item.FromDocumentId, item.ToDocumentId, item.CodeNorm))
            .ToArray();

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            innerCt => CreateManyCoreAsync(items, innerCt),
            ct);
    }

    private async Task<int> CreateManyCoreAsync(IReadOnlyList<BatchItem> items, CancellationToken ct)
    {
        var documentIds = items
            .SelectMany(static item => new[] { item.FromDocumentId, item.ToDocumentId })
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();

        await LockDocumentsDeterministicallyAsync(locks, documentIds, ct);
        var documentsById = await documents.GetForUpdateByIdsAsync(documentIds, ct);
        foreach (var documentId in documentIds)
        {
            if (!documentsById.ContainsKey(documentId))
                throw new DocumentNotFoundException(documentId);
        }

        foreach (var item in items)
        {
            var fromDocument = documentsById[item.FromDocumentId];
            var toDocument = documentsById[item.ToDocumentId];
            EnsureRelationshipAllowed(
                item.Type,
                item.FromDocumentId,
                item.ToDocumentId,
                fromDocument.TypeCode,
                toDocument.TypeCode);
            EnsureDraftRequirements(item.Type, fromDocument, toDocument);
        }

        var directedItems = items
            .SelectMany(static item => item.Type.IsBidirectional
                ? new[] { item, item.Reverse() }
                : [item])
            .DistinctBy(static item => (item.FromDocumentId, item.ToDocumentId, item.CodeNorm))
            .ToArray();

        EnsureNoBatchCardinalityConflicts(directedItems);
        EnsureNoCyclesWithinBatch(directedItems);

        var cycleItems = directedItems
            .Where(static item => !item.Type.IsBidirectional)
            .ToArray();
        if (cycleItems.Length > 0)
        {
            var cycleIndexes = await relationships.FindCycleCreatingRequestIndexesAsync(
                cycleItems
                    .Select(static item => new DocumentRelationshipCycleCheck(
                        item.FromDocumentId,
                        item.ToDocumentId,
                        item.CodeNorm))
                    .ToArray(),
                CycleGuardMaxDepth,
                ct);

            if (cycleIndexes.Count > 0)
            {
                var failed = cycleItems[cycleIndexes[0]];
                throw new DocumentRelationshipValidationException(
                    reason: "cycle_detected",
                    relationshipCode: failed.Type.Code,
                    fromDocumentId: failed.FromDocumentId,
                    toDocumentId: failed.ToDocumentId);
            }
        }

        var cardinalityItems = directedItems
            .Where(static item => item.Type.MaxOutgoingPerFrom == 1 || item.Type.MaxIncomingPerTo == 1)
            .ToArray();
        if (cardinalityItems.Length > 0)
        {
            var existingRelationships = await relationships.GetCardinalityConflictsAsync(
                cardinalityItems
                    .Select(static item => new DocumentRelationshipCardinalityCheck(
                        item.FromDocumentId,
                        item.ToDocumentId,
                        item.CodeNorm,
                        CheckOutgoing: item.Type.MaxOutgoingPerFrom == 1,
                        CheckIncoming: item.Type.MaxIncomingPerTo == 1))
                    .ToArray(),
                ct);

            foreach (var item in cardinalityItems)
                EnforceBatchCardinality(item, existingRelationships);
        }

        var nowUtc = timeProvider.GetUtcNowDateTime();
        var records = directedItems
            .Select(item => new DocumentRelationshipRecord
            {
                Id = DeterministicDocumentRelationshipId.FromNormalizedCode(
                    item.FromDocumentId,
                    item.CodeNorm,
                    item.ToDocumentId),
                FromDocumentId = item.FromDocumentId,
                ToDocumentId = item.ToDocumentId,
                RelationshipCode = item.Code,
                RelationshipCodeNorm = item.CodeNorm,
                CreatedAtUtc = nowUtc
            })
            .ToArray();

        var createdIds = (await relationships.TryCreateManyAsync(records, ct)).ToHashSet();
        if (createdIds.Count == 0)
            return 0;

        var auditRequests = records
            .Where(record => createdIds.Contains(record.Id))
            .Select(record => new AuditLogWriteRequest(
                AuditEntityKind.DocumentRelationship,
                record.Id,
                AuditActionCodes.DocumentRelationshipCreate,
                [
                    AuditLogService.Change("from_document_id", null, record.FromDocumentId),
                    AuditLogService.Change("to_document_id", null, record.ToDocumentId),
                    AuditLogService.Change("relationship_code", null, record.RelationshipCode),
                    AuditLogService.Change("relationship_code_norm", null, record.RelationshipCodeNorm)
                ],
                new
                {
                    fromDocumentId = record.FromDocumentId,
                    toDocumentId = record.ToDocumentId,
                    relationshipCode = record.RelationshipCode
                }))
            .ToArray();

        await audit.WriteBatchAsync(auditRequests, ct);
        return createdIds.Count;
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

    public async Task<bool> ExistsIncomingAsync(
        Guid toDocumentId,
        string relationshipCode,
        CancellationToken ct = default)
    {
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        var codeNorm = NormalizeCodeNorm(NormalizeCode(relationshipCode));
        return await relationships.GetSingleIncomingByCodeNormAsync(toDocumentId, codeNorm, ct) is not null;
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

    private static void EnforceBatchCardinality(
        BatchItem item,
        IReadOnlyList<DocumentRelationshipRecord> existingRelationships)
    {
        if (item.Type.MaxOutgoingPerFrom == 1)
        {
            var existingOutgoing = existingRelationships.FirstOrDefault(relationship =>
                relationship.FromDocumentId == item.FromDocumentId
                && string.Equals(relationship.RelationshipCodeNorm, item.CodeNorm, StringComparison.Ordinal)
                && relationship.ToDocumentId != item.ToDocumentId);

            if (existingOutgoing is not null)
            {
                throw new DocumentRelationshipValidationException(
                    reason: "cardinality_max_outgoing_per_from",
                    relationshipCode: item.Type.Code,
                    fromDocumentId: item.FromDocumentId,
                    toDocumentId: item.ToDocumentId,
                    extraContext: new Dictionary<string, object?>
                    {
                        ["existingToDocumentId"] = existingOutgoing.ToDocumentId
                    });
            }
        }

        if (item.Type.MaxIncomingPerTo == 1)
        {
            var existingIncoming = existingRelationships.FirstOrDefault(relationship =>
                relationship.ToDocumentId == item.ToDocumentId
                && string.Equals(relationship.RelationshipCodeNorm, item.CodeNorm, StringComparison.Ordinal)
                && relationship.FromDocumentId != item.FromDocumentId);

            if (existingIncoming is not null)
            {
                throw new DocumentRelationshipValidationException(
                    reason: "cardinality_max_incoming_per_to",
                    relationshipCode: item.Type.Code,
                    fromDocumentId: item.FromDocumentId,
                    toDocumentId: item.ToDocumentId,
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

    private static async Task LockDocumentsDeterministicallyAsync(
        IAdvisoryLockManager locks,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct)
    {
        if (locks is IAdvisoryLockBatchManager batchLocks)
        {
            await batchLocks.LockDocumentsAsync(documentIds, ct);
            return;
        }

        foreach (var documentId in documentIds.Distinct().OrderBy(static id => id))
        {
            await locks.LockDocumentAsync(documentId, ct);
        }
    }

    private static void EnsureNoBatchCardinalityConflicts(IReadOnlyList<BatchItem> items)
    {
        var outgoingConflict = items
            .Where(static item => item.Type.MaxOutgoingPerFrom == 1)
            .GroupBy(static item => (item.FromDocumentId, item.CodeNorm))
            .FirstOrDefault(group => group.Select(static item => item.ToDocumentId).Distinct().Skip(1).Any());

        if (outgoingConflict is not null)
        {
            var item = outgoingConflict.First();
            throw new DocumentRelationshipValidationException(
                reason: "cardinality_max_outgoing_per_from",
                relationshipCode: item.Type.Code,
                fromDocumentId: item.FromDocumentId,
                toDocumentId: item.ToDocumentId);
        }

        var incomingConflict = items
            .Where(static item => item.Type.MaxIncomingPerTo == 1)
            .GroupBy(static item => (item.ToDocumentId, item.CodeNorm))
            .FirstOrDefault(group => group.Select(static item => item.FromDocumentId).Distinct().Skip(1).Any());

        if (incomingConflict is not null)
        {
            var item = incomingConflict.First();
            throw new DocumentRelationshipValidationException(
                reason: "cardinality_max_incoming_per_to",
                relationshipCode: item.Type.Code,
                fromDocumentId: item.FromDocumentId,
                toDocumentId: item.ToDocumentId);
        }
    }

    private static void EnsureNoCyclesWithinBatch(IReadOnlyList<BatchItem> items)
    {
        foreach (var codeGroup in items
                     .Where(static item => !item.Type.IsBidirectional)
                     .GroupBy(static item => item.CodeNorm))
        {
            var edges = codeGroup
                .GroupBy(static item => item.FromDocumentId)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.DistinctBy(static item => item.ToDocumentId).ToArray());
            var states = new Dictionary<Guid, byte>();

            foreach (var start in edges.Keys)
            {
                if (states.GetValueOrDefault(start) != 0)
                    continue;

                states[start] = 1;
                var pending = new Stack<(Guid Node, int NextEdgeIndex)>();
                pending.Push((start, 0));

                while (pending.TryPop(out var frame))
                {
                    if (!edges.TryGetValue(frame.Node, out var targets) || frame.NextEdgeIndex >= targets.Length)
                    {
                        states[frame.Node] = 2;
                        continue;
                    }

                    pending.Push((frame.Node, frame.NextEdgeIndex + 1));
                    var edge = targets[frame.NextEdgeIndex];
                    var targetState = states.GetValueOrDefault(edge.ToDocumentId);

                    if (targetState == 1)
                    {
                        throw new DocumentRelationshipValidationException(
                            reason: "cycle_detected",
                            relationshipCode: edge.Type.Code,
                            fromDocumentId: edge.FromDocumentId,
                            toDocumentId: edge.ToDocumentId);
                    }

                    if (targetState != 0)
                        continue;

                    states[edge.ToDocumentId] = 1;
                    pending.Push((edge.ToDocumentId, 0));
                }
            }
        }
    }

    private sealed record BatchItem(
        Guid FromDocumentId,
        Guid ToDocumentId,
        DocumentRelationshipTypeDefinition Type,
        string Code,
        string CodeNorm)
    {
        public BatchItem Reverse() => this with
        {
            FromDocumentId = ToDocumentId,
            ToDocumentId = FromDocumentId
        };
    }
}
