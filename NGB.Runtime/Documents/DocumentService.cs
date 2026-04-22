using System.Globalization;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.AuditLog;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Common;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.Ui;
using NGB.Runtime.Validation;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using DocumentStatus = NGB.Contracts.Metadata.DocumentStatus;

namespace NGB.Runtime.Documents;

/// <summary>
/// Universal, metadata-driven document CRUD.
///
/// Scope:
/// - supports scalar fields in the head table (doc_*)
/// - supports tabular parts (doc_*__*) with draft replace semantics (DELETE + INSERT)
/// - posting/actions/graph delegate to runtime services; effects return current effective state for posted documents
///
/// IMPORTANT: This service persists documents via the common registry (documents) + typed head table.
/// </summary>
public sealed class DocumentService(
    IUnitOfWork uow,
    IDocumentRepository documents,
    IDocumentDraftService drafts,
    IDocumentTypeRegistry documentTypes,
    IDocumentReader reader,
    IDocumentPartsReader partsReader,
    IDocumentPartsWriter partsWriter,
    IDocumentWriter writer,
    IDocumentPostingService posting,
    IDocumentDerivationService derivations,
    IDocumentPostingActionResolver postingActionResolver,
    IDocumentOperationalRegisterPostingActionResolver opregPostingActionResolver,
    IDocumentReferenceRegisterPostingActionResolver refregPostingActionResolver,
    IEnumerable<IDocumentUiEffectsContributor> uiEffectsContributors,
    IDocumentRelationshipGraphReadService relationshipGraph,
    IReferencePayloadEnricher refEnricher,
    IEnumerable<IDocumentDraftPayloadValidator> draftPayloadValidators,
    TimeProvider? timeProvider = null,
    IAuditLogService? audit = null,
    IDocumentEffectsQueryService? effectsQuery = null)
    : IDocumentService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public Task<IReadOnlyList<DocumentTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct)
    {
        var list = documentTypes.GetAll()
            .OrderBy(x => x.TypeCode, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentTypeMetadataDto>>(list);
    }

    public Task<DocumentTypeMetadataDto> GetTypeMetadataAsync(string documentType, CancellationToken ct)
        => Task.FromResult(ToDto(GetModel(documentType).Meta));

    public async Task<PageResponseDto<DocumentDto>> GetPageAsync(
        string documentType,
        PageRequestDto request,
        CancellationToken ct)
    {
        var model = GetModel(documentType);
        var (softDeleteMode, scalarFilters) = ExtractSoftDeleteFilter(request.Filters);
        var query = BuildQuery(model, request.Search, scalarFilters) with { SoftDeleteFilterMode = softDeleteMode };

        var total = await reader.CountAsync(model.Head, query, ct);
        var rows = await reader.GetPageAsync(model.Head, query, request.Offset, request.Limit, ct);
        IReadOnlyList<DocumentDto> items = rows.Select(r => ToDto(model, r, parts: null)).ToList();

        if (items.Count > 0)
            items = await refEnricher.EnrichDocumentItemsAsync(model.Head, model.Meta.TypeCode, items, ct);

        return new PageResponseDto<DocumentDto>(items, request.Offset, request.Limit, (int)total);
    }

    public async Task<DocumentDto> GetByIdAsync(string documentType, Guid id, CancellationToken ct)
    {
        var model = GetModel(documentType);
        id.EnsureRequired(nameof(id));

        var row = await reader.GetByIdAsync(model.Head, id, ct);
        if (row is null)
            throw new DocumentNotFoundException(id);

        var parts = await ReadPartsAsync(model, id, ct);
        var item = ToDto(model, row, parts);
        var enriched = await refEnricher.EnrichDocumentItemsAsync(model.Head, model.Meta.TypeCode, [item], ct);
        return enriched[0];
    }

    public async Task<IReadOnlyList<DocumentDerivationActionDto>> GetDerivationActionsAsync(
        string documentType,
        Guid id,
        CancellationToken ct)
    {
        var model = GetModel(documentType);
        id.EnsureRequired(nameof(id));

        var row = await reader.GetByIdAsync(model.Head, id, ct);
        if (row is null)
            throw new DocumentNotFoundException(id);

        var actions = await derivations.ListActionsForDocumentAsync(id, ct);
        return actions.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<DocumentLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> docTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct)
    {
        if (docTypes is null)
            throw new NgbArgumentRequiredException(nameof(docTypes));

        if (perTypeLimit <= 0 || docTypes.Count == 0)
            return [];

        var heads = ResolveDistinctLookupHeads(docTypes);
        if (heads.Count == 0)
            return [];

        var rows = await reader.LookupAcrossTypesAsync(heads, query, perTypeLimit, activeOnly, ct);
        return MapDocumentLookups(rows);
    }

    public async Task<IReadOnlyList<DocumentLookupDto>> GetByIdsAcrossTypesAsync(
        IReadOnlyList<string> docTypes,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        if (docTypes is null)
            throw new NgbArgumentRequiredException(nameof(docTypes));

        if (ids is null)
            throw new NgbArgumentRequiredException(nameof(ids));

        if (docTypes.Count == 0 || ids.Count == 0)
            return [];

        var heads = ResolveDistinctLookupHeads(docTypes);
        if (heads.Count == 0)
            return [];

        var rows = await reader.GetByIdsAcrossTypesAsync(heads, ids, ct);
        return MapDocumentLookups(rows);
    }

    private IReadOnlyList<DocumentHeadDescriptor> ResolveDistinctLookupHeads(IReadOnlyList<string> docTypes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heads = new List<DocumentHeadDescriptor>(docTypes.Count);

        foreach (var documentType in docTypes)
        {
            if (string.IsNullOrWhiteSpace(documentType) || !seen.Add(documentType))
                continue;

            heads.Add(GetModel(documentType).Head);
        }

        return heads;
    }

    private static IReadOnlyList<DocumentLookupDto> MapDocumentLookups(IReadOnlyList<DocumentLookupRow> rows)
        => rows
            .Select(row => new DocumentLookupDto(
                Id: row.Id,
                DocumentType: row.TypeCode,
                Display: row.Label,
                Status: ToContractStatus(row.Status),
                IsMarkedForDeletion: row.IsMarkedForDeletion,
                Number: row.Number))
            .ToList();

    public async Task<DocumentDto> CreateDraftAsync(string documentType, RecordPayload payload, CancellationToken ct)
    {
        var model = GetModel(documentType);
        var (partTablesToWrite, partRowsByTable, partRowsByPartCode) = ParseAndValidateParts(model, payload);

        var id = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await ValidateDraftPayloadAsync(model, documentId: null, isCreate: true, payload, partRowsByPartCode, innerCt);
            var fieldValues = ParseAndValidateFields(model, payload, requireAllRequired: true);

            // Generic documents don't expose number/date in the HTTP contract yet.
            // Use UTC now and let numbering policies assign a number if configured.
            var newId = await drafts.CreateDraftAsync(
                typeCode: model.Meta.TypeCode,
                number: null,
                dateUtc: _timeProvider.GetUtcNowDateTime(),
                manageTransaction: false,
                suppressAudit: audit is not null,
                ct: innerCt);

            await writer.UpsertHeadAsync(model.Head, newId, fieldValues, innerCt);

            if (partTablesToWrite.Count > 0)
                await partsWriter.ReplacePartsAsync(partTablesToWrite, newId, partRowsByTable, innerCt);

            if (audit is not null)
            {
                var created = await GetByIdAsync(documentType, newId, innerCt);
                var changes = DocumentAuditChangeBuilder.BuildCreateChanges(created, model.Meta.Presentation);
                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: newId,
                    actionCode: AuditActionCodes.DocumentCreateDraft,
                    changes: changes,
                    metadata: new { typeCode = model.Meta.TypeCode },
                    ct: innerCt);
            }

            return newId;
        }, ct);

        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> UpdateDraftAsync(
        string documentType,
        Guid id,
        RecordPayload payload,
        CancellationToken ct)
    {
        var model = GetModel(documentType);
        id.EnsureRequired(nameof(id));

        var (partTablesToWrite, partRowsByTable, partRowsByPartCode) = ParseAndValidateParts(model, payload);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await documents.GetForUpdateAsync(id, innerCt)
                         ?? throw new DocumentNotFoundException(id);

            // Guard: the request URL (documentType) must match the actual document type stored in the header row.
            if (!string.Equals(locked.TypeCode, model.Meta.TypeCode, StringComparison.OrdinalIgnoreCase))
                throw new NgbArgumentInvalidException(nameof(documentType),
                    $"Document '{id}' belongs to '{locked.TypeCode}', not '{model.Meta.TypeCode}'.");

            if (locked.Status == Core.Documents.DocumentStatus.MarkedForDeletion)
            {
                throw new DocumentMarkedForDeletionException(
                    operation: "Document.UpdateDraft",
                    documentId: id,
                    markedForDeletionAtUtc: locked.MarkedForDeletionAtUtc ?? _timeProvider.GetUtcNowDateTime());
            }

            if (locked.Status != Core.Documents.DocumentStatus.Draft)
            {
                throw new DocumentWorkflowStateMismatchException(
                    operation: "Document.UpdateDraft",
                    documentId: id,
                    expectedState: nameof(Core.Documents.DocumentStatus.Draft),
                    actualState: locked.Status.ToString());
            }

            DocumentDto? beforeAudit = null;
            if (audit is not null)
                beforeAudit = await GetByIdAsync(documentType, id, innerCt);

            await ValidateDraftPayloadAsync(model, documentId: id, isCreate: false, payload, partRowsByPartCode, innerCt);

            // Partial update: only provided fields are updated.
            // IMPORTANT: to keep NOT NULL invariants stable and to make UPSERT resilient,
            // we always include required head columns (filled from existing record if not provided).
            var fieldValues = ParseAndValidateFields(model, payload, requireAllRequired: false);

            var requiredByName = model.ScalarColumns
                .Where(c => c.Required)
                .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

            foreach (var v in fieldValues)
            {
                if (v.Value is null && requiredByName.ContainsKey(v.ColumnName))
                    throw new NgbArgumentInvalidException($"payload.Fields.{v.ColumnName}",
                        ValidationMessageFormatter.RequiredFieldMessage(GetLabel(requiredByName[v.ColumnName])));
            }

            if (requiredByName.Count > 0)
            {
                var existing = await reader.GetByIdAsync(model.Head, id, innerCt);
                if (existing is null)
                    throw new DocumentNotFoundException(id);

                var provided = fieldValues
                    .Select(v => v.ColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var merged = new List<DocumentHeadValue>(fieldValues);

                foreach (var (name, col) in requiredByName)
                {
                    if (provided.Contains(name))
                        continue;

                    existing.Fields.TryGetValue(name, out var cur);
                    if (cur is null)
                        throw new NgbConfigurationViolationException(
                            $"Document '{id}' has missing required head value '{name}' in '{model.Head.HeadTableName}'.");

                    merged.Add(new DocumentHeadValue(col.ColumnName, col.Type, cur));
                }

                fieldValues = merged;
            }

            await writer.UpsertHeadAsync(model.Head, id, fieldValues, innerCt);

            if (partTablesToWrite.Count > 0)
                await partsWriter.ReplacePartsAsync(partTablesToWrite, id, partRowsByTable, innerCt);

            // Touch the common header row to update updated_at_utc.
            var now = _timeProvider.GetUtcNowDateTime();
            await documents.UpdateDraftHeaderAsync(id, locked.Number, locked.DateUtc, now, innerCt);

            if (audit is not null && beforeAudit is not null)
            {
                var afterAudit = await GetByIdAsync(documentType, id, innerCt);
                var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(beforeAudit, afterAudit, model.Meta.Presentation);
                if (changes.Count > 0)
                {
                    await audit.WriteAsync(
                        entityKind: AuditEntityKind.Document,
                        entityId: id,
                        actionCode: AuditActionCodes.DocumentUpdateDraft,
                        changes: changes,
                        metadata: new { typeCode = model.Meta.TypeCode },
                        ct: innerCt);
                }
            }
        }, ct);

        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task DeleteDraftAsync(string documentType, Guid id, CancellationToken ct)
    {
        var model = GetModel(documentType);
        id.EnsureRequired(nameof(id));

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await documents.GetForUpdateAsync(id, innerCt);
            if (locked is null)
                return;

            if (!string.Equals(locked.TypeCode, model.Meta.TypeCode, StringComparison.OrdinalIgnoreCase))
                throw new NgbArgumentInvalidException(nameof(documentType),
                    $"Document '{id}' belongs to '{locked.TypeCode}', not '{model.Meta.TypeCode}'.");

            await drafts.DeleteDraftAsync(id, manageTransaction: false, ct: innerCt);
        }, ct);
    }

    public async Task<DocumentDto> PostAsync(string documentType, Guid id, CancellationToken ct)
    {
        await posting.PostAsync(id, ct);
        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> UnpostAsync(string documentType, Guid id, CancellationToken ct)
    {
        await posting.UnpostAsync(id, ct);
        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> RepostAsync(string documentType, Guid id, CancellationToken ct)
    {
        // Repost is a workflow operation (requires Posted state). For register-only documents
        // the accounting posting handler may be absent, and must NOT be resolved eagerly.
        var doc = await documents.GetAsync(id, ct) ?? throw new DocumentNotFoundException(id);
        var action = postingActionResolver.TryResolve(doc);
        await posting.RepostAsync(
            id,
            async (ctx, innerCt) =>
            {
                if (action is null)
                    throw new DocumentPostingHandlerNotConfiguredException(id, doc.TypeCode);

                await action(ctx, innerCt);
            },
            ct);
        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> MarkForDeletionAsync(string documentType, Guid id, CancellationToken ct)
    {
        await posting.MarkForDeletionAsync(id, ct);
        return await GetByIdAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> UnmarkForDeletionAsync(string documentType, Guid id, CancellationToken ct)
    {
        await posting.UnmarkForDeletionAsync(id, ct);
        return await GetByIdAsync(documentType, id, ct);
    }

    public Task<DocumentDto> ExecuteActionAsync(string documentType, Guid id, string actionCode, CancellationToken ct)
        => throw new DocumentActionsNotSupportedException(documentType, actionCode);

    public async Task<RelationshipGraphDto> GetRelationshipGraphAsync(
        string documentType,
        Guid id,
        int depth,
        int maxNodes,
        CancellationToken ct)
    {
        // Document Flow UI must render from a single API request.
        // Backend must also avoid N+1 typed-head fetches, so we batch non-root head rows in one multi-type read.
        var rootModel = GetModel(documentType);
        var rootHead = await reader.GetByIdAsync(rootModel.Head, id, ct)
            ?? throw new DocumentNotFoundException(id);

        var graph = await relationshipGraph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: id,
                MaxDepth: depth,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: maxNodes,
                MaxEdges: 500),
            ct);

        var typeById = graph.Nodes.ToDictionary(n => n.DocumentId, n => n.TypeCode);

        string BuildNodeId(Guid docId)
        {
            if (!typeById.TryGetValue(docId, out var t) || string.IsNullOrWhiteSpace(t))
                t = "document";

            return $"doc:{t}:{docId}";
        }

        var modelsByType = graph.Nodes
            .Select(n => n.TypeCode)
            .Append(documentType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(typeCode => typeCode, GetModel, StringComparer.OrdinalIgnoreCase);

        var headRowsById = await LoadGraphHeadRowsAsync(graph.Nodes, id, rootHead, modelsByType, ct);

        var nodes = new List<GraphNodeDto>(graph.Nodes.Count);
        foreach (var n in graph.Nodes)
        {
            headRowsById.TryGetValue(n.DocumentId, out var headRow);

            var shortId = n.DocumentId.ToString("N")[..8];
            var fallbackTitle = !string.IsNullOrWhiteSpace(n.Number)
                ? $"{n.TypeCode} {n.Number}"
                : $"{n.TypeCode} {shortId}";

            var title = !string.IsNullOrWhiteSpace(headRow?.Display)
                ? headRow.Display!
                : fallbackTitle;

            var subtitle = n.DateUtc.ToString("yyyy-MM-dd");
            var status = headRow is not null
                ? ToContractStatus(headRow.Status)
                : ToContractStatus(n.Status);
            var amountField = modelsByType.TryGetValue(n.TypeCode, out var model)
                ? model.AmountField
                : null;
            var amount = TryExtractDocumentAmount(headRow, amountField);

            nodes.Add(new GraphNodeDto(
                NodeId: BuildNodeId(n.DocumentId),
                Kind: EntityKind.Document,
                TypeCode: n.TypeCode,
                EntityId: n.DocumentId,
                Title: title,
                Subtitle: subtitle,
                DocumentStatus: status,
                Depth: n.Depth,
                Amount: amount));
        }

        // Safety: ensure root is always present even if the persistence reader returns an empty node set.
        if (nodes.All(n => n.EntityId != id))
        {
            nodes.Insert(0, new GraphNodeDto(
                NodeId: $"doc:{documentType}:{id}",
                Kind: EntityKind.Document,
                TypeCode: documentType,
                EntityId: id,
                Title: rootHead.Display ?? id.ToString("N"),
                Subtitle: null,
                DocumentStatus: ToContractStatus(rootHead.Status),
                Depth: 0,
                Amount: TryExtractDocumentAmount(rootHead, rootModel.AmountField)));
        }

        var edges = graph.Edges
            .Select(e => new GraphEdgeDto(
                FromNodeId: BuildNodeId(e.FromDocumentId),
                ToNodeId: BuildNodeId(e.ToDocumentId),
                RelationshipType: e.RelationshipCode,
                Label: null))
            .ToList();

        return new RelationshipGraphDto(nodes, edges);
    }

    private async Task<Dictionary<Guid, DocumentHeadRow>> LoadGraphHeadRowsAsync(
        IReadOnlyList<DocumentRelationshipGraphNode> graphNodes,
        Guid rootId,
        DocumentHeadRow rootHead,
        IReadOnlyDictionary<string, DocumentModel> modelsByType,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, DocumentHeadRow>(graphNodes.Count)
        {
            [rootId] = rootHead
        };

        var nonRootNodes = graphNodes
            .Where(n => n.DocumentId != rootId)
            .ToArray();

        if (nonRootNodes.Length == 0)
            return result;

        var heads = nonRootNodes
            .Select(n => n.TypeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(typeCode => modelsByType[typeCode].Head)
            .ToList();

        var ids = nonRootNodes
            .Select(n => n.DocumentId)
            .Distinct()
            .ToList();

        var rows = await reader.GetHeadRowsByIdsAcrossTypesAsync(heads, ids, ct);

        foreach (var row in rows)
        {
            result[row.Id] = row;
        }

        return result;
    }

    private static decimal? TryExtractDocumentAmount(DocumentHeadRow? document, string? amountField)
    {
        if (document?.Fields is null || string.IsNullOrWhiteSpace(amountField))
            return null;

        if (document.Fields.TryGetValue(amountField, out var value)
            && TryGetDecimal(value, out var amount))
        {
            return amount;
        }

        return null;
    }

    private static bool TryGetDecimal(object? value, out decimal amount)
    {
        switch (value)
        {
            case null:
                break;
            case decimal d:
                amount = d;
                return true;
            case byte b:
                amount = b;
                return true;
            case short s:
                amount = s;
                return true;
            case int i:
                amount = i;
                return true;
            case long l:
                amount = l;
                return true;
            case float f:
                amount = (decimal)f;
                return true;
            case double db:
                amount = (decimal)db;
                return true;
            case string raw when !string.IsNullOrWhiteSpace(raw):
                raw = raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
                    || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                {
                    return true;
                }
                break;
            case JsonElement json:
                return TryGetDecimal(json, out amount);
            case IConvertible convertible:
                try
                {
                    amount = convertible.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    break;
                }
        }

        amount = default;
        return false;
    }

    private static bool TryGetDecimal(JsonElement value, out decimal amount)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out amount);
            case JsonValueKind.String:
            {
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    break;

                raw = raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
                return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
                    || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
            }
        }

        amount = default;
        return false;
    }

    public Task<DocumentEffectsDto> GetEffectsAsync(string documentType, Guid id, int limit, CancellationToken ct)
        => GetEffectsInternalAsync(documentType, id, limit, ct);

    private async Task<DocumentEffectsDto> GetEffectsInternalAsync(
        string documentType,
        Guid id,
        int limit,
        CancellationToken ct)
    {
        var model = GetModel(documentType);
        id.EnsureRequired(nameof(id));

        // PM-specific UI contributors use typed readers that require an active transaction.
        // Effects endpoint is read-only, but we still wrap it in a UoW transaction for consistency.
        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction: !uow.HasActiveTransaction,
            async innerCt =>
            {
                var row = await reader.GetByIdAsync(model.Head, id, innerCt);
                if (row is null)
                    throw new DocumentNotFoundException(id);

                var dto = ToDto(model, row, parts: null);

                // Base document record (registry) is needed to resolve posting handlers.
                var record = await documents.GetAsync(id, innerCt);
                if (record is null)
                    throw new DocumentNotFoundException(id);

                var ui = await BuildUiEffectsAsync(documentType, dto, record, innerCt);
                var sections = effectsQuery is null
                    ? new DocumentEffectsQueryResult([], [], [])
                    : await effectsQuery.GetAsync(record, limit, innerCt);

                return new DocumentEffectsDto(
                    sections.AccountingEntries,
                    sections.OperationalRegisterMovements,
                    sections.ReferenceRegisterWrites,
                    ui);
            },
            ct);
    }

    private async Task<DocumentUiEffectsDto> BuildUiEffectsAsync(
        string documentType,
        DocumentDto doc,
        DocumentRecord record,
        CancellationToken ct)
    {
        // Determine whether posting is supported by any handler.
        var hasAccounting = postingActionResolver.TryResolve(record) is not null;
        var hasOpreg = opregPostingActionResolver.TryResolve(record) is not null;
        var hasRefreg = refregPostingActionResolver.TryResolve(record) is not null;
        var hasPosting = hasAccounting || hasOpreg || hasRefreg;

        var disabled = new Dictionary<string, IReadOnlyList<DocumentUiActionReasonDto>>(StringComparer.OrdinalIgnoreCase);

        static DocumentUiActionReasonDto R(string code, string msg) => new(code, msg);

        var isPosted = doc.Status == DocumentStatus.Posted;
        var isDraft = doc.Status == DocumentStatus.Draft;

        var canEdit = isDraft && !doc.IsMarkedForDeletion;
        if (!canEdit)
        {
            if (doc.IsMarkedForDeletion)
                disabled["edit"] = [R("document.deleted", "Document is deleted.")];
            else
                disabled["edit"] = [R("document.not_draft", "Document can be edited only in Draft status.")];
        }

        var canPost = isDraft && !doc.IsMarkedForDeletion && hasPosting;
        if (!canPost)
        {
            var reasons = new List<DocumentUiActionReasonDto>();
            
            if (doc.IsMarkedForDeletion)
                reasons.Add(R("document.deleted", "Document is deleted."));
            
            if (!isDraft)
                reasons.Add(R("document.not_draft", "Only Draft documents can be posted."));
            
            if (isDraft && !hasPosting)
                reasons.Add(R("document.posting_not_configured", "Posting is not configured for this document type."));
            
            if (reasons.Count > 0)
                disabled["post"] = reasons;
        }

        var canUnpost = isPosted && hasPosting;
        if (!canUnpost)
        {
            var reasons = new List<DocumentUiActionReasonDto>();
            
            if (doc.IsMarkedForDeletion)
                reasons.Add(R("document.deleted", "Document is deleted."));
            
            if (!isPosted)
                reasons.Add(R("document.not_posted", "Only Posted documents can be unposted."));
            
            if (isPosted && !hasPosting)
                reasons.Add(R("document.posting_not_configured", "Posting is not configured for this document type."));
            
            if (reasons.Count > 0)
                disabled["unpost"] = reasons;
        }

        var canRepost = isPosted && hasPosting;
        if (!canRepost)
        {
            var reasons = new List<DocumentUiActionReasonDto>();
            if (doc.IsMarkedForDeletion)
                reasons.Add(R("document.deleted", "Document is deleted."));
            
            if (!isPosted)
                reasons.Add(R("document.not_posted", "Only Posted documents can be reposted."));
            
            if (isPosted && !hasPosting)
                reasons.Add(R("document.posting_not_configured", "Posting is not configured for this document type."));
            
            if (reasons.Count > 0)
                disabled["repost"] = reasons;
        }

        // Domain-specific, module-contributed action. Base is disabled (no reasons).
        var canApply = false;

        foreach (var c in uiEffectsContributors)
        {
            var list = await c.ContributeAsync(documentType, doc.Id, doc.Payload, doc.Status, ct);
            foreach (var x in list)
            {
                if (!string.Equals(x.Action, "apply", StringComparison.OrdinalIgnoreCase))
                    continue;

                canApply = x.IsAllowed;
                if (x is { IsAllowed: false, DisabledReasons.Count: > 0 })
                    disabled["apply"] = x.DisabledReasons;
                else
                    disabled.Remove("apply");
            }
        }

        return new DocumentUiEffectsDto(
            IsPosted: isPosted,
            CanEdit: canEdit,
            CanPost: canPost,
            CanUnpost: canUnpost,
            CanRepost: canRepost,
            CanApply: canApply,
            DisabledReasons: disabled);
    }

    public Task<DocumentDto> DeriveAsync(
        string targetDocumentType,
        Guid sourceDocumentId,
        string relationshipType,
        RecordPayload? initialPayload,
        CancellationToken ct)
    {
        // Validate the target document type up front so callers keep getting the same fast-fail
        // behavior as the regular CRUD endpoints.
        _ = GetModel(targetDocumentType);
        sourceDocumentId.EnsureRequired(nameof(sourceDocumentId));

        if (string.IsNullOrWhiteSpace(relationshipType))
            throw new NgbArgumentRequiredException(nameof(relationshipType));

        var normalizedRelationshipType = relationshipType.Trim();

        return DeriveInternalAsync(
            targetDocumentType,
            sourceDocumentId,
            normalizedRelationshipType,
            initialPayload,
            ct);
    }

    private async Task<DocumentDto> DeriveInternalAsync(
        string targetDocumentType,
        Guid sourceDocumentId,
        string relationshipType,
        RecordPayload? initialPayload,
        CancellationToken ct)
    {
        var source = await documents.GetAsync(sourceDocumentId, ct)
            ?? throw new DocumentNotFoundException(sourceDocumentId);

        var matches = derivations.ListActionsForSourceType(source.TypeCode)
            .Where(action =>
                string.Equals(action.ToTypeCode, targetDocumentType, StringComparison.OrdinalIgnoreCase)
                && action.RelationshipCodes.Any(code => string.Equals(code, relationshipType, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
        {
            // Backward compatibility: before explicit derivation definitions existed, this route acted as a
            // lightweight scaffold that created a new draft from the supplied initial payload.
            // Keep that V0 behavior only when callers actually provide a payload; otherwise fail fast so
            // real derivation-based flows still surface missing configuration clearly.
            if (initialPayload is not null)
                return await CreateDraftAsync(targetDocumentType, initialPayload, ct);

            throw new DocumentDerivationNotFoundException($"{source.TypeCode}->{targetDocumentType}[{relationshipType}]");
        }

        if (matches.Length > 1)
        {
            throw new NgbConfigurationViolationException(
                message: "Multiple document derivations match the requested source, target, and relationship type.",
                context: new Dictionary<string, object?>
                {
                    ["sourceTypeCode"] = source.TypeCode,
                    ["targetTypeCode"] = targetDocumentType,
                    ["relationshipType"] = relationshipType,
                    ["derivationCodes"] = matches.Select(action => action.Code).ToArray()
                });
        }

        var derivedId = await derivations.CreateDraftAsync(
            derivationCode: matches[0].Code,
            createdFromDocumentId: sourceDocumentId,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: ct);

        if (initialPayload is not null)
            return await UpdateDraftAsync(targetDocumentType, derivedId, initialPayload, ct);

        return await GetByIdAsync(targetDocumentType, derivedId, ct);
    }

    private static DocumentDerivationActionDto ToDto(DocumentDerivationAction action)
        => new(
            Code: action.Code,
            Name: action.Name,
            FromTypeCode: action.FromTypeCode,
            ToTypeCode: action.ToTypeCode,
            RelationshipCodes: action.RelationshipCodes);

    private static DocumentQuery BuildQuery(
        DocumentModel model,
        string? search,
        IReadOnlyDictionary<string, string>? filters)
    {
        var f = new List<DocumentFilter>();
        DateOnly? periodFrom = null;
        DateOnly? periodTo = null;
        var allowedFilters = model.ListFilters
            .ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

        if (filters is { Count: > 0 })
        {
            foreach (var (rawKey, v) in filters)
            {
                var k = NormalizeFilterKey(rawKey);

                if (IsPeriodFromKey(k))
                {
                    periodFrom = ParseDateOnlyFilter(v, rawKey);
                    continue;
                }

                if (IsPeriodToKey(k))
                {
                    periodTo = ParseDateOnlyFilter(v, rawKey);
                    continue;
                }

                if (!allowedFilters.TryGetValue(k, out var filter))
                    throw new NgbArgumentInvalidException(nameof(filters),
                        $"Filter '{ToLabel(rawKey, ColumnType.String)}' is not available for this document list.");

                f.Add(new DocumentFilter(
                    Key: filter.Key,
                    Values: ParseFilterValues(v, rawKey, filter.IsMulti),
                    ValueType: filter.Type,
                    HeadColumnName: ResolveFilterHeadColumnName(model, filter)));
            }
        }

        return new DocumentQuery(search, f)
        {
            PeriodFilter = BuildPeriodFilter(model, periodFrom, periodTo)
        };
    }

    private static bool IsPeriodFromKey(string key)
        => string.Equals(key, "periodFrom", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "period_from", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "fromMonth", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "from_month", StringComparison.OrdinalIgnoreCase);

    private static bool IsPeriodToKey(string key)
        => string.Equals(key, "periodTo", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "period_to", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "toMonth", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "to_month", StringComparison.OrdinalIgnoreCase);

    private static DateOnly ParseDateOnlyFilter(string? value, string keyName)
    {
        var s = (value ?? string.Empty).Trim();
        if (s.Length == 0)
            throw new NgbArgumentInvalidException(keyName, $"'{keyName}' is required.");

        if (DateOnly.TryParse(s, out var d))
            return d;

        throw new NgbArgumentInvalidException(keyName, $"'{keyName}' must be a valid date.");
    }

    private static DocumentPeriodFilter? BuildPeriodFilter(DocumentModel model, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null)
            return null;

        if (from > to)
            throw new NgbArgumentInvalidException("periodFrom", "'periodFrom' must be less than or equal to 'periodTo'.");

        var columnName = ResolvePeriodColumnName(model);
        if (columnName is null)
            throw new NgbConfigurationViolationException(
                $"Document '{model.Meta.TypeCode}' does not define a date column for period filtering.",
                context: new Dictionary<string, object?> { ["documentType"] = model.Meta.TypeCode });

        return new DocumentPeriodFilter(columnName, from, to);
    }

    private static string? ResolvePeriodColumnName(DocumentModel model)
    {
        var dateColumns = model.ScalarColumns
            .Where(c => c.Type is ColumnType.Date or ColumnType.DateTimeUtc)
            .Select(c => c.ColumnName)
            .ToList();

        if (dateColumns.Count == 0)
            return null;

        string[] preferred =
        [
            "due_on_utc",
            "received_on_utc",
            "applied_on_utc",
            "start_on_utc",
            "period_from_utc",
            "date_utc",
            "document_date_utc",
            "occurred_on_utc",
            "posted_on_utc"
        ];

        foreach (var name in preferred)
        {
            var found = dateColumns.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }

        var notRangeEnd = dateColumns.FirstOrDefault(c =>
            !c.Contains("end", StringComparison.OrdinalIgnoreCase)
            && !c.Contains("to_utc", StringComparison.OrdinalIgnoreCase));
        
        if (notRangeEnd is not null)
            return notRangeEnd;

        return dateColumns[0];
    }

    private static string? ResolveFilterHeadColumnName(DocumentModel model, DocumentListFilterMetadata filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.HeadColumnName))
            return filter.HeadColumnName;

        var directColumn = model.ScalarColumns
            .FirstOrDefault(x => string.Equals(x.ColumnName, filter.Key, StringComparison.OrdinalIgnoreCase));

        return directColumn?.ColumnName;
    }

    private static IReadOnlyList<string> ParseFilterValues(string? rawValue, string keyName, bool isMulti)
    {
        var values = isMulti
            ? (rawValue ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [(rawValue ?? string.Empty).Trim()];

        var normalized = values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            throw new NgbArgumentInvalidException(keyName, $"'{keyName}' requires at least one value.");

        return normalized;
    }

    private static (SoftDeleteFilterMode Mode, IReadOnlyDictionary<string, string>? ScalarFilters) ExtractSoftDeleteFilter(
        IReadOnlyDictionary<string, string>? filters)
    {
        if (filters is null || filters.Count == 0)
            return (SoftDeleteFilterMode.All, null);

        var mode = SoftDeleteFilterMode.All;
        var rest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawKey, value) in filters)
        {
            var key = NormalizeFilterKey(rawKey);

            if (string.Equals(key, "deleted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "trash", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseSoftDeleteMode(value, rawKey);
                continue;
            }

            rest[key] = value;
        }

        return (mode, rest.Count == 0 ? null : rest);
    }

    private static string NormalizeFilterKey(string key)
        => key.StartsWith("filters.", StringComparison.OrdinalIgnoreCase) ? key["filters.".Length..] : key;

    private static SoftDeleteFilterMode ParseSoftDeleteMode(string? value, string keyName)
    {
        var s = (value ?? string.Empty).Trim();

        if (s.Length == 0 || string.Equals(s, "all", StringComparison.OrdinalIgnoreCase))
            return SoftDeleteFilterMode.All;

        if (string.Equals(s, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "0", StringComparison.OrdinalIgnoreCase))
        {
            return SoftDeleteFilterMode.Active;
        }

        if (string.Equals(s, "deleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "1", StringComparison.OrdinalIgnoreCase))
        {
            return SoftDeleteFilterMode.Deleted;
        }

        throw new NgbArgumentInvalidException(keyName, $"Unknown '{keyName}' value '{s}'. Use 'active', 'deleted', or 'all'.");
    }

    private static (IReadOnlyList<DocumentTableMetadata> PartTablesToWrite,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsByTable,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsByPartCode)
        ParseAndValidateParts(DocumentModel model, RecordPayload payload)
    {
        var parts = payload.Parts;
        if (parts is null || parts.Count == 0)
        {
            return ([],
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase));
        }

        var partTables = model.Meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .ToList();

        if (partTables.Count == 0)
            throw new NgbArgumentInvalidException(nameof(payload), "This document does not support tabular parts.");

        var tableByPartCode = new Dictionary<string, DocumentTableMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in partTables)
        {
            var partCode = t.GetRequiredPartCode(model.Meta.TypeCode);
            if (!tableByPartCode.TryAdd(partCode, t))
                throw new NgbConfigurationViolationException($"Document '{model.Meta.TypeCode}' has duplicate part code '{partCode}'.");
        }

        foreach (var key in parts.Keys)
        {
            if (!tableByPartCode.ContainsKey(key))
                throw new NgbArgumentInvalidException(key,
                    $"Part '{GetPartLabel(key)}' is not available on this form.");
        }

        var tablesToWrite = new List<DocumentTableMetadata>();
        var rowsByTable = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        var rowsByPartCode = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (partCode, partPayload) in parts)
        {
            var table = tableByPartCode[partCode];
            tablesToWrite.Add(table);

            var known = table.Columns
                .Where(c => !IsDocumentId(c.ColumnName) && c.Type != ColumnType.Json)
                .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

            var required = table.Columns
                .Where(c => !IsDocumentId(c.ColumnName) && c.Type != ColumnType.Json && c.Required)
                .ToList();

            var rows = partPayload?.Rows ?? [];
            var typedRows = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
            var partLabel = GetPartLabel(partCode);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNumber = i + 1;
                var rowPath = $"{partCode}[{i}]";
                if (row is null)
                    throw new NgbArgumentInvalidException(rowPath, $"{partLabel} row {rowNumber} is invalid.");

                foreach (var key in row.Keys)
                {
                    if (IsDocumentId(key))
                        throw new NgbArgumentInvalidException($"{rowPath}.document_id",
                            $"Document Id is managed automatically and cannot be set in {partLabel} row {rowNumber}.");

                    if (!known.ContainsKey(key))
                        throw new NgbArgumentInvalidException($"{rowPath}.{key}",
                            $"Field '{ToLabel(key, ColumnType.String)}' is not available in {partLabel} row {rowNumber}.");
                }

                // Required columns must be present and non-null.
                foreach (var col in required)
                {
                    var fieldPath = $"{rowPath}.{col.ColumnName}";
                    if (!row.TryGetValue(col.ColumnName, out var el))
                        throw new NgbArgumentInvalidException(fieldPath,
                            $"{GetLabel(col)} is required in {partLabel} row {rowNumber}.");

                    var val = ConvertJsonValue(el, col.Type, fieldPath, GetLabel(col));
                    if (val is null)
                        throw new NgbArgumentInvalidException(fieldPath,
                            $"{GetLabel(col)} is required in {partLabel} row {rowNumber}.");
                }

                var typed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, col) in known)
                {
                    if (!row.TryGetValue(name, out var el))
                        continue;

                    var fieldPath = $"{rowPath}.{name}";
                    var val = ConvertJsonValue(el, col.Type, fieldPath, GetLabel(col));

                    if (col.Required && val is null)
                        throw new NgbArgumentInvalidException(fieldPath,
                            $"{GetLabel(col)} is required in {partLabel} row {rowNumber}.");

                    typed[name] = val;
                }

                typedRows.Add(typed);
            }

            rowsByTable[table.TableName] = typedRows;
            rowsByPartCode[partCode] = typedRows;
        }

        return (tablesToWrite, rowsByTable, rowsByPartCode);
    }

    private async Task ValidateDraftPayloadAsync(
        DocumentModel model,
        Guid? documentId,
        bool isCreate,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        // Validators are optional. When none are registered, this is a cheap no-op.
        foreach (var v in draftPayloadValidators)
        {
            if (!string.Equals(v.TypeCode, model.Meta.TypeCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isCreate)
            {
                await v.ValidateCreateDraftPayloadAsync(payload, typedPartRowsByPartCode, ct);
                continue;
            }

            if (documentId is null)
                throw new NgbInvariantViolationException("Draft payload validation for update requires documentId.");

            await v.ValidateUpdateDraftPayloadAsync(documentId.Value, payload, typedPartRowsByPartCode, ct);
        }
    }

    private DocumentModel GetModel(string documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new NgbArgumentRequiredException(nameof(documentType));

        var meta = documentTypes.TryGet(documentType);
        if (meta is null)
            throw new DocumentTypeNotFoundException(documentType);

        var headTable = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Document '{meta.TypeCode}' has no Head table metadata.");

        var columns = headTable.Columns
            .Where(x => !string.Equals(x.ColumnName, "document_id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var amountField = NormalizeOptionalMetadataField(meta.Presentation?.AmountField);
        if (amountField is not null)
        {
            var amountColumn = columns.FirstOrDefault(x => string.Equals(x.ColumnName, amountField, StringComparison.OrdinalIgnoreCase));
            if (amountColumn is null)
            {
                throw new NgbConfigurationViolationException(
                    $"Document '{meta.TypeCode}' declares Presentation.AmountField '{amountField}' but no such head column exists.");
            }

            if (!IsSupportedAmountColumnType(amountColumn.Type))
            {
                throw new NgbConfigurationViolationException(
                    $"Document '{meta.TypeCode}' declares Presentation.AmountField '{amountField}' with unsupported type '{amountColumn.Type}'.");
            }

            amountField = amountColumn.ColumnName;
        }

        var head = meta.CreateHeadDescriptor();

        return new DocumentModel(meta, columns, meta.ListFilters ?? [], head, amountField);
    }

    private static IReadOnlyList<DocumentHeadValue> ParseAndValidateFields(
        DocumentModel model,
        RecordPayload payload,
        bool requireAllRequired)
    {
        var fields = payload.Fields ?? new Dictionary<string, JsonElement>();

        var known = model.ScalarColumns
            .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var key in fields.Keys)
        {
            if (!known.ContainsKey(key))
                throw new NgbArgumentInvalidException(nameof(payload),
                    $"Field '{ToLabel(key, ColumnType.String)}' is not available on this form.");
        }

        var result = new List<DocumentHeadValue>();

        foreach (var col in model.ScalarColumns)
        {
            if (!fields.TryGetValue(col.ColumnName, out var el))
            {
                if (requireAllRequired && col.Required)
                    throw new NgbArgumentInvalidException($"payload.Fields.{col.ColumnName}",
                        ValidationMessageFormatter.RequiredFieldMessage(GetLabel(col)));

                continue;
            }

            var value = ConvertJsonValue(
                el,
                col.Type,
                $"payload.Fields.{col.ColumnName}",
                GetLabel(col));

            if (requireAllRequired && col.Required && value is null)
                throw new NgbArgumentInvalidException($"payload.Fields.{col.ColumnName}",
                    ValidationMessageFormatter.RequiredFieldMessage(GetLabel(col)));

            result.Add(new DocumentHeadValue(col.ColumnName, col.Type, value));
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement el, ColumnType type, string name)
        => ConvertJsonValue(el, type, name, ValidationMessageFormatter.ToLabel(ExtractFieldKey(name), type));

    private static object? ConvertJsonValue(JsonElement el, ColumnType type, string name, string label)
    {
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        try
        {
            return type switch
            {
                ColumnType.String => el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : el.ToString(),

                ColumnType.Int32 => el.ValueKind == JsonValueKind.Number
                    ? el.GetInt32()
                    : int.Parse(el.GetString() ?? el.ToString(), CultureInfo.InvariantCulture),

                ColumnType.Int64 => el.ValueKind == JsonValueKind.Number
                    ? el.GetInt64()
                    : long.Parse(el.GetString() ?? el.ToString(), CultureInfo.InvariantCulture),

                ColumnType.Decimal => el.ValueKind == JsonValueKind.Number
                    ? el.GetDecimal()
                    : ParseDecimalInvariantStrict(el.GetString() ?? el.ToString()),

                ColumnType.Boolean => el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False
                    ? el.GetBoolean()
                    : bool.Parse(el.GetString() ?? el.ToString()),

                ColumnType.Guid => el.ParseGuidOrRef(),

                ColumnType.Date => DateOnly.Parse(el.GetString() ?? el.ToString(), CultureInfo.InvariantCulture),

                ColumnType.DateTimeUtc => ParseUtc(el, name),

                ColumnType.Json => el.GetRawText(),

                _ => el.ToString()
            };
        }
        catch
        {
            throw new NgbArgumentInvalidException(name, ValidationMessageFormatter.InvalidValueMessage(label, type));
        }
    }

    private static string ExtractFieldKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var idx = path.LastIndexOf(".", StringComparison.Ordinal);
        return idx >= 0 && idx + 1 < path.Length
            ? path[(idx + 1)..]
            : path;
    }

    private static DateTime ParseUtc(JsonElement el, string name)
    {
        var s = el.GetString() ?? el.ToString();
        var dt = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        dt.EnsureUtc(name);
        return dt;
    }

    private static decimal ParseDecimalInvariantStrict(string s)
    {
        if (decimal.TryParse(
                s,
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return value;
        }

        throw new NgbArgumentInvalidException(nameof(s), "Value must be a valid decimal in invariant format.");
    }

    private static DocumentDto ToDto(
        DocumentModel model,
        DocumentHeadRow row,
        IReadOnlyDictionary<string, RecordPartPayload>? parts)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in model.ScalarColumns)
        {
            row.Fields.TryGetValue(col.ColumnName, out var value);
            fields[col.ColumnName] = JsonTools.J(value);
        }

        var payload = new RecordPayload(fields, parts);

        return new DocumentDto(
            Id: row.Id,
            Display: row.Display,
            Payload: payload,
            Status: ToContractStatus(row.Status),
            IsMarkedForDeletion: row.IsMarkedForDeletion,
            Number: row.Number);
    }

    private static DocumentStatus ToContractStatus(Core.Documents.DocumentStatus status)
        => status switch
        {
            Core.Documents.DocumentStatus.Draft => DocumentStatus.Draft,
            Core.Documents.DocumentStatus.Posted => DocumentStatus.Posted,
            Core.Documents.DocumentStatus.MarkedForDeletion => DocumentStatus.MarkedForDeletion,
            _ => DocumentStatus.Draft
        };

    private static bool IsDocumentId(string name)
        => string.Equals(name, "document_id", StringComparison.OrdinalIgnoreCase);

    private static DocumentTypeMetadataDto ToDto(DocumentTypeMetadata meta)
    {
        var head = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head);
        var columns = head?.Columns
            .Where(x => !string.Equals(x.ColumnName, "document_id", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        var amountField = NormalizeOptionalMetadataField(meta.Presentation?.AmountField);

        static string ToTitle(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return code;

            var parts = code.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        }

        var listColumnDefs = columns
            .Where(x => x.Type != ColumnType.Json)
            .Take(6)
            .ToList();

        if (amountField is not null)
        {
            AppendPreferredListColumnIfMissing(
                listColumnDefs,
                columns,
                x => string.Equals(x.ColumnName, amountField, StringComparison.OrdinalIgnoreCase));
        }

        if (!listColumnDefs.Any(x => x.Type is ColumnType.Date or ColumnType.DateTimeUtc))
        {
            AppendPreferredListColumnIfMissing(
                listColumnDefs,
                columns,
                x => x.Type is ColumnType.Date or ColumnType.DateTimeUtc);
        }

        var listCols = listColumnDefs
            .Select(c => new ColumnMetadataDto(
                Key: c.ColumnName,
                Label: GetLabel(c),
                DataType: ToDataType(c.Type),
                Lookup: ToLookupDto(c.Lookup),
                Options: ToOptionsDto(c.Options)))
            .ToList();

        var listFilters = (meta.ListFilters ?? [])
            .Select(filter => new ListFilterFieldDto(
                Key: filter.Key,
                Label: filter.Label,
                DataType: ToFilterDataType(filter),
                IsMulti: filter.IsMulti,
                Lookup: ToLookupDto(filter.Lookup),
                Options: filter.Options?.Select(x => new MetadataOptionDto(x.Value, x.Label)).ToList(),
                Description: filter.Description))
            .ToList();

        var presentation = meta.Presentation;
        var displayIsReadOnly = presentation?.ComputedDisplay == true;

        var formFields = columns
            .Where(x => x.Type != ColumnType.Json)
            .Select(c => new FieldMetadataDto(
                Key: c.ColumnName,
                Label: GetLabel(c),
                DataType: ToDataType(c.Type),
                UiControl: ToUiControl(c.Type),
                IsRequired: c.Required,
                IsReadOnly: displayIsReadOnly && string.Equals(c.ColumnName, "display", StringComparison.OrdinalIgnoreCase),
                Lookup: ToLookupDto(c.Lookup),
                Validation: c.MaxLength.HasValue ? new FieldValidationDto(MaxLength: c.MaxLength) : null,
                Options: ToOptionsDto(c.Options),
                MirroredRelationship: ToMirroredRelationshipDto(c.MirroredRelationship)))
            .Select(f => new FormRowDto([f]))
            .ToList();

        var parts = meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .Select(t =>
            {
                var partCode = t.GetRequiredPartCode(meta.TypeCode);
                var partColumns = t.Columns
                    .Where(c => !IsDocumentId(c.ColumnName) && c.Type != ColumnType.Json)
                    .ToList();

                var partListCols = partColumns
                    .Select(c => new ColumnMetadataDto(
                        Key: c.ColumnName,
                        Label: GetLabel(c),
                        DataType: ToDataType(c.Type),
                        Lookup: ToLookupDto(c.Lookup),
                        Options: ToOptionsDto(c.Options)))
                    .ToList();

                return new PartMetadataDto(
                    PartCode: partCode,
                    Title: ToTitle(partCode),
                    List: new ListMetadataDto(partListCols));
            })
            .ToList();

        return new DocumentTypeMetadataDto(
            DocumentType: meta.TypeCode,
            DisplayName: meta.Presentation?.DisplayName ?? meta.TypeCode,
            Kind: EntityKind.Document,
            Icon: null,
            List: new ListMetadataDto(listCols, listFilters.Count == 0 ? null : listFilters),
            Form: new FormMetadataDto([new FormSectionDto("Main", formFields)]),
            Parts: parts.Count == 0 ? null : parts,
            Actions: null,
            Presentation: meta.Presentation is null
                ? null
                : new DocumentPresentationDto(
                    DisplayName: meta.Presentation.DisplayName,
                    HasNumber: meta.Presentation.HasNumber,
                    ComputedDisplay: meta.Presentation.ComputedDisplay,
                    HideSystemFieldsInEditor: meta.Presentation.HideSystemFieldsInEditor,
                    AmountField: amountField),
            Capabilities: new DocumentCapabilitiesDto(SupportsActions: false));
    }

    private static void AppendPreferredListColumnIfMissing(
        List<DocumentColumnMetadata> listColumnDefs,
        IReadOnlyList<DocumentColumnMetadata> allColumns,
        Func<DocumentColumnMetadata, bool> predicate)
    {
        if (listColumnDefs.Any(predicate))
            return;

        var preferredColumn = allColumns.FirstOrDefault(x => x.Type != ColumnType.Json
            && !listColumnDefs.Contains(x)
            && predicate(x));

        if (preferredColumn is not null)
            listColumnDefs.Add(preferredColumn);
    }

    private static LookupSourceDto? ToLookupDto(LookupSourceMetadata? lookup)
        => lookup switch
        {
            CatalogLookupSourceMetadata catalog => new CatalogLookupSourceDto(catalog.CatalogType),
            DocumentLookupSourceMetadata document => new DocumentLookupSourceDto(document.DocumentTypes),
            ChartOfAccountsLookupSourceMetadata => new ChartOfAccountsLookupSourceDto(),
            null => null,
            _ => throw new NgbConfigurationViolationException($"Unsupported lookup source metadata type '{lookup.GetType().Name}'.")
        };

    private static IReadOnlyList<MetadataOptionDto>? ToOptionsDto(IReadOnlyList<FieldOptionMetadata>? options)
        => options?.Select(x => new MetadataOptionDto(x.Value, x.Label)).ToList();

    private static MirroredDocumentRelationshipDto? ToMirroredRelationshipDto(
        MirroredDocumentRelationshipMetadata? mirroredRelationship)
        => mirroredRelationship is null
            ? null
            : new MirroredDocumentRelationshipDto(mirroredRelationship.RelationshipCode);

    private static DataType ToDataType(ColumnType type)
        => type switch
        {
            ColumnType.String => DataType.String,
            ColumnType.Guid => DataType.Guid,
            ColumnType.Int32 => DataType.Int32,
            ColumnType.Int64 => DataType.Int32,
            ColumnType.Decimal => DataType.Decimal,
            ColumnType.Boolean => DataType.Boolean,
            ColumnType.Date => DataType.Date,
            ColumnType.DateTimeUtc => DataType.DateTime,
            _ => DataType.String
        };

    private static DataType ToFilterDataType(DocumentListFilterMetadata filter)
    {
        if (filter.Lookup is not null)
            return DataType.Lookup;

        if (filter.Options is { Count: > 0 })
            return DataType.Enum;

        return ToDataType(filter.Type);
    }

    private static UiControl ToUiControl(ColumnType type)
        => type switch
        {
            ColumnType.Boolean => UiControl.Checkbox,
            ColumnType.Int32 or ColumnType.Int64 or ColumnType.Decimal => UiControl.Number,
            ColumnType.Date => UiControl.Date,
            ColumnType.DateTimeUtc => UiControl.DateTime,
            _ => UiControl.Input
        };

    private static string ToLabel(string key, ColumnType type)
        => ValidationMessageFormatter.ToLabel(key, type);

    private static string GetLabel(DocumentColumnMetadata column)
        => string.IsNullOrWhiteSpace(column.UiLabel)
            ? ToLabel(column.ColumnName, column.Type)
            : column.UiLabel!;

    private static string GetPartLabel(string partCode)
        => ToLabel(partCode, ColumnType.String);

    private static string? NormalizeOptionalMetadataField(string? fieldName)
        => string.IsNullOrWhiteSpace(fieldName)
            ? null
            : fieldName.Trim();

    private static bool IsSupportedAmountColumnType(ColumnType type)
        => type is ColumnType.Decimal or ColumnType.Int32 or ColumnType.Int64;

    private sealed record DocumentModel(
        DocumentTypeMetadata Meta,
        IReadOnlyList<DocumentColumnMetadata> ScalarColumns,
        IReadOnlyList<DocumentListFilterMetadata> ListFilters,
        DocumentHeadDescriptor Head,
        string? AmountField);

    private async Task<IReadOnlyDictionary<string, RecordPartPayload>?> ReadPartsAsync(
        DocumentModel model,
        Guid documentId,
        CancellationToken ct)
    {
        var partTables = model.Meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .ToList();

        if (partTables.Count == 0)
            return null;

        var rowsByTable = await partsReader.GetPartsAsync(partTables, documentId, ct);

        var parts = new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in partTables)
        {
            var partCode = t.GetRequiredPartCode(model.Meta.TypeCode);
            rowsByTable.TryGetValue(t.TableName, out var rows);
            rows ??= Array.Empty<IReadOnlyDictionary<string, object?>>();

            var partRows = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Count);

            foreach (var r in rows)
            {
                var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

                foreach (var c in t.Columns)
                {
                    if (IsDocumentId(c.ColumnName) || c.Type == ColumnType.Json)
                        continue;

                    r.TryGetValue(c.ColumnName, out var value);
                    row[c.ColumnName] = JsonTools.J(value);
                }

                partRows.Add(row);
            }

            parts[partCode] = new RecordPartPayload(partRows);
        }

        return parts;
    }
}
