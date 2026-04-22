using System.Globalization;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs.Validation;
using NGB.Runtime.Ui;
using NGB.Runtime.Validation;
using NGB.Runtime.UnitOfWork;
using NGB.Tools;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Catalogs;

/// <summary>
/// Universal, metadata-driven catalog CRUD.
///
/// Application/orchestration layer:
/// - catalog types come from <see cref="ICatalogTypeRegistry"/> (Definitions-backed)
/// - persistence is delegated to <see cref="ICatalogReader"/> / <see cref="ICatalogWriter"/>
///   and <see cref="ICatalogPartsReader"/> / <see cref="ICatalogPartsWriter"/>
///
/// Scope:
/// - supports scalar fields in the head table (cat_*)
/// - supports tabular parts (cat_*__*) with replace semantics on specified parts
/// </summary>
public sealed class CatalogService(
    IUnitOfWork uow,
    ICatalogRepository repo,
    ICatalogDraftService drafts,
    ICatalogTypeRegistry catalogTypes,
    ICatalogReader reader,
    ICatalogPartsReader partsReader,
    ICatalogPartsWriter partsWriter,
    ICatalogWriter writer,
    ICatalogValidatorResolver validators,
    IReferencePayloadEnricher refEnricher,
    TimeProvider timeProvider,
    IAuditLogService? audit = null)
    : ICatalogService
{
    public Task<IReadOnlyList<CatalogTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct)
    {
        var list = catalogTypes.All()
            .OrderBy(x => x.CatalogCode, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogTypeMetadataDto>>(list);
    }

    public Task<CatalogTypeMetadataDto> GetTypeMetadataAsync(string catalogType, CancellationToken ct)
        => Task.FromResult(ToDto(GetModel(catalogType).Meta));

    public async Task<PageResponseDto<CatalogItemDto>> GetPageAsync(
        string catalogType,
        PageRequestDto request,
        CancellationToken ct)
    {
        var model = GetModel(catalogType);
        var (softDeleteMode, scalarFilters) = ExtractSoftDeleteFilter(request.Filters);
        var query = BuildQuery(model, request.Search, scalarFilters) with { SoftDeleteFilterMode = softDeleteMode };

        var total = await reader.CountAsync(model.Head, query, ct);
        var rows = await reader.GetPageAsync(model.Head, query, request.Offset, request.Limit, ct);
        IReadOnlyList<CatalogItemDto> items = rows.Select(r => ToItemDto(model, r, parts: null)).ToList();

        if (items.Count > 0)
            items = await refEnricher.EnrichCatalogItemsAsync(model.Head, model.Meta.CatalogCode, items, ct);

        return new PageResponseDto<CatalogItemDto>(items, request.Offset, request.Limit, (int)total);
    }

    public async Task<CatalogItemDto> GetByIdAsync(string catalogType, Guid id, CancellationToken ct)
    {
        var model = GetModel(catalogType);
        id.EnsureRequired(nameof(id));

        var row = await reader.GetByIdAsync(model.Head, id, ct);
        if (row is null)
            throw new CatalogNotFoundException(id);

        var parts = await ReadPartsAsync(model, id, ct);
        var item = ToItemDto(model, row, parts);
        var enriched = await refEnricher.EnrichCatalogItemsAsync(model.Head, model.Meta.CatalogCode, [item], ct);
        return enriched[0];
    }

    public async Task<IReadOnlyList<CatalogLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> catalogTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct)
    {
        if (catalogTypes is null)
            throw new NgbArgumentRequiredException(nameof(catalogTypes));

        if (perTypeLimit <= 0 || catalogTypes.Count == 0)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heads = new List<CatalogHeadDescriptor>(catalogTypes.Count);

        foreach (var catalogType in catalogTypes)
        {
            if (string.IsNullOrWhiteSpace(catalogType) || !seen.Add(catalogType))
                continue;

            heads.Add(GetModel(catalogType).Head);
        }

        if (heads.Count == 0)
            return [];

        var rows = await reader.LookupAcrossTypesAsync(heads, query, perTypeLimit, activeOnly, ct);

        return rows
            .Select(row => new CatalogLookupDto(
                Id: row.Id,
                CatalogType: row.CatalogCode,
                Display: row.Label,
                IsMarkedForDeletion: row.IsMarkedForDeletion))
            .ToList();
    }

    public async Task<CatalogItemDto> CreateAsync(string catalogType, RecordPayload payload, CancellationToken ct)
    {
        var model = GetModel(catalogType);
        var (partTablesToWrite, partRowsByTable) = ParseAndValidateParts(model, payload);

        var fieldValues = ParseAndValidateFields(model, payload, requireAllRequired: true);
        var effectiveForValidation = fieldValues
            .ToDictionary(v => v.ColumnName, v => v.Value, StringComparer.OrdinalIgnoreCase);

        var upsertValidators = validators.ResolveUpsertValidators(model.Meta.CatalogCode);

        var id = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // IMPORTANT:
            // Create only the catalog header row here (catalogs).
            // The typed head row (cat_*) is created by the subsequent UpsertHeadAsync using the validated payload.
            // This supports catalogs with strict NOT NULL / CHECK invariants that cannot be satisfied
            // by a "draft placeholder" typed row.
            var newId = await drafts.CreateHeaderOnlyAsync(
                model.Meta.CatalogCode,
                manageTransaction: false,
                suppressAudit: audit is not null,
                ct: innerCt);

            if (upsertValidators.Count > 0)
            {
                var ctx = new NGB.Definitions.Catalogs.Validation.CatalogUpsertValidationContext(
                    model.Meta.CatalogCode,
                    newId,
                    IsCreate: true,
                    effectiveForValidation);

                foreach (var v in upsertValidators)
                    await v.ValidateUpsertAsync(ctx, innerCt);
            }

            await writer.UpsertHeadAsync(model.Head, newId, fieldValues, innerCt);
            if (partTablesToWrite.Count > 0)
                await partsWriter.ReplacePartsAsync(partTablesToWrite, newId, partRowsByTable, innerCt);

            await repo.TouchAsync(newId, timeProvider.GetUtcNowDateTime(), innerCt);

            if (audit is not null)
            {
                var createdItem = await GetByIdAsync(catalogType, newId, innerCt);
                var changes = CatalogAuditChangeBuilder.BuildCreateChanges(createdItem, model.Meta.CatalogCode);
                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Catalog,
                    entityId: newId,
                    actionCode: AuditActionCodes.CatalogCreate,
                    changes: changes,
                    metadata: new { catalogCode = model.Meta.CatalogCode },
                    ct: innerCt);
            }

            return newId;
        }, ct);

        return await GetByIdAsync(catalogType, id, ct);
    }

    public async Task<CatalogItemDto> UpdateAsync(
        string catalogType,
        Guid id,
        RecordPayload payload,
        CancellationToken ct)
    {
        var model = GetModel(catalogType);
        id.EnsureRequired(nameof(id));
        var (partTablesToWrite, partRowsByTable) = ParseAndValidateParts(model, payload);

        var upsertValidators = validators.ResolveUpsertValidators(model.Meta.CatalogCode);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await repo.GetForUpdateAsync(id, innerCt);
            if (locked is null)
                throw new CatalogNotFoundException(id);

            // Guard: the request URL (catalogType) must match the actual catalog type stored in the header row.
            // This check must run BEFORE any payload validation to avoid confusing "Unknown field" errors when the ID
            // belongs to a different catalog type.
            if (!string.Equals(locked.CatalogCode, model.Meta.CatalogCode, StringComparison.OrdinalIgnoreCase))
                throw new NgbArgumentInvalidException(nameof(catalogType),
                    $"Catalog '{id}' belongs to '{locked.CatalogCode}', not '{model.Meta.CatalogCode}'.");

            if (locked.IsDeleted)
                throw new NgbArgumentInvalidException(nameof(id), $"Catalog '{id}' is marked for deletion.");

            // Partial update: only provided fields are updated.
            // IMPORTANT:
            // Catalog writer uses an UPSERT (INSERT .. ON CONFLICT .. DO UPDATE).
            // PostgreSQL validates NOT NULL / CHECK constraints on the *INSERT* row even if the row already exists
            // and the statement ends up in the DO UPDATE path.
            //
            // Therefore, for partial updates we must build a *full* head row value set (existing + updates),
            // otherwise catalogs with strict invariants (like pm.property kind/address rules) would fail on update.
            var updates = ParseAndValidateFields(model, payload, requireAllRequired: false);

            // Enforce required columns: if a required column is present in the payload, it must not be null.
            var requiredByName = model.ScalarColumns
                .Where(c => c.Required)
                .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

            foreach (var v in updates)
            {
                if (v.Value is null && requiredByName.TryGetValue(v.ColumnName, out var value))
                    throw new NgbArgumentInvalidException($"payload.Fields.{v.ColumnName}",
                        ValidationMessageFormatter.RequiredFieldMessage(GetLabel(value)));
            }

            var existing = await reader.GetByIdAsync(model.Head, id, innerCt);
            if (existing is null)
                throw new CatalogNotFoundException(id);

            CatalogItemDto? beforeAudit = null;
            if (audit is not null)
            {
                var beforeParts = await ReadPartsAsync(model, id, innerCt);
                var beforeItem = ToItemDto(model, existing, beforeParts);
                var enrichedBefore = await refEnricher.EnrichCatalogItemsAsync(
                    model.Head,
                    model.Meta.CatalogCode,
                    [beforeItem],
                    innerCt);
                beforeAudit = enrichedBefore[0];
            }

            // Build the effective field set for validation: existing fields merged with payload updates.
            var effective = new Dictionary<string, object?>(existing.Fields, StringComparer.OrdinalIgnoreCase);
            foreach (var v in updates)
            {
                effective[v.ColumnName] = v.Value;
            }

            if (upsertValidators.Count > 0)
            {
                var ctx = new NGB.Definitions.Catalogs.Validation.CatalogUpsertValidationContext(
                    model.Meta.CatalogCode,
                    id,
                    IsCreate: false,
                    effective);

                foreach (var v in upsertValidators)
                {
                    await v.ValidateUpsertAsync(ctx, innerCt);
                }
            }

            // Build a full value set for UPSERT: keep unspecified columns as-is.
            // Required columns must not be NULL in the existing row either.
            var provided = updates
                .Select(v => v.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fieldValues = new List<CatalogHeadValue>(updates);

            foreach (var col in model.ScalarColumns)
            {
                if (provided.Contains(col.ColumnName))
                    continue;

                effective.TryGetValue(col.ColumnName, out var cur);

                if (cur is null && col.Required)
                    throw new NgbConfigurationViolationException(
                        $"Catalog '{id}' has missing required head value '{col.ColumnName}' in '{model.Head.HeadTableName}'.");

                fieldValues.Add(new CatalogHeadValue(col.ColumnName, col.ColumnType, cur));
            }

            await writer.UpsertHeadAsync(model.Head, id, fieldValues, innerCt);
            if (partTablesToWrite.Count > 0)
                await partsWriter.ReplacePartsAsync(partTablesToWrite, id, partRowsByTable, innerCt);

            await repo.TouchAsync(id, timeProvider.GetUtcNowDateTime(), innerCt);

            if (audit is not null && beforeAudit is not null)
            {
                var afterAudit = await GetByIdAsync(catalogType, id, innerCt);
                var changes = CatalogAuditChangeBuilder.BuildUpdateChanges(beforeAudit, afterAudit);
                if (changes.Count > 0)
                {
                    await audit.WriteAsync(
                        entityKind: AuditEntityKind.Catalog,
                        entityId: id,
                        actionCode: AuditActionCodes.CatalogUpdate,
                        changes: changes,
                        metadata: new { catalogCode = model.Meta.CatalogCode },
                        ct: innerCt);
                }
            }
        }, ct);

        return await GetByIdAsync(catalogType, id, ct);
    }

    public async Task MarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct)
    {
        var model = GetModel(catalogType);
        id.EnsureRequired(nameof(id));

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await repo.GetForUpdateAsync(id, innerCt);
            if (locked is null)
                throw new CatalogNotFoundException(id);

            if (!string.Equals(locked.CatalogCode, model.Meta.CatalogCode, StringComparison.OrdinalIgnoreCase))
                throw new NgbArgumentInvalidException(nameof(catalogType),
                    $"Catalog '{id}' belongs to '{locked.CatalogCode}', not '{model.Meta.CatalogCode}'.");

            await drafts.MarkForDeletionAsync(id, manageTransaction: false, ct: innerCt);
        }, ct);
    }

    public async Task UnmarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct)
    {
        var model = GetModel(catalogType);
        id.EnsureRequired(nameof(id));

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await repo.GetForUpdateAsync(id, innerCt);
            if (locked is null)
                throw new CatalogNotFoundException(id);

            if (!string.Equals(locked.CatalogCode, model.Meta.CatalogCode, StringComparison.OrdinalIgnoreCase))
                throw new NgbArgumentInvalidException(nameof(catalogType),
                    $"Catalog '{id}' belongs to '{locked.CatalogCode}', not '{model.Meta.CatalogCode}'.");

            await drafts.UnmarkForDeletionAsync(id, manageTransaction: false, ct: innerCt);
        }, ct);
    }

    public async Task<IReadOnlyList<LookupItemDto>> LookupAsync(
        string catalogType,
        string? query,
        int limit,
        CancellationToken ct)
    {
        var model = GetModel(catalogType);

        if (limit <= 0)
            return [];

        var rows = await reader.LookupAsync(model.Head, query, limit, ct);
        return rows.Select(x => new LookupItemDto(x.Id, x.Label)).ToList();
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetByIdsAsync(
        string catalogType,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        var model = GetModel(catalogType);
        if (ids.Count == 0)
            return [];

        var rows = await reader.GetByIdsAsync(model.Head, ids, ct);

        return rows.Select(x => new LookupItemDto(x.Id, x.Label)).ToList();
    }

    private CatalogModel GetModel(string catalogType)
    {
        if (string.IsNullOrWhiteSpace(catalogType))
            throw new NgbArgumentRequiredException(nameof(catalogType));

        var meta = catalogTypes.GetRequired(catalogType);

        var headTable = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has no Head table metadata.");

        var displayColumn = meta.Presentation.DisplayColumn;
        if (string.IsNullOrWhiteSpace(displayColumn))
            throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has empty Presentation.DisplayColumn.");

        var scalarColumns = headTable.Columns
            .Where(x => !string.Equals(x.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var head = new CatalogHeadDescriptor(
            CatalogCode: meta.CatalogCode,
            HeadTableName: headTable.TableName,
            DisplayColumn: displayColumn,
            Columns: scalarColumns
                .Select(c => new CatalogHeadColumn(c.ColumnName, c.ColumnType))
                .ToList());

        return new CatalogModel(meta, scalarColumns, head);
    }

    private static (IReadOnlyList<CatalogTableMetadata> PartTablesToWrite,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsByTable)
        ParseAndValidateParts(CatalogModel model, RecordPayload payload)
    {
        var parts = payload.Parts;
        if (parts is null || parts.Count == 0)
        {
            return ([],
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase));
        }

        var partTables = model.Meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .ToList();

        if (partTables.Count == 0)
            throw new NgbArgumentInvalidException(nameof(payload), "This catalog does not support tabular parts.");

        var tableByPartCode = new Dictionary<string, CatalogTableMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in partTables)
        {
            var partCode = t.GetRequiredPartCode(model.Meta.CatalogCode);
            if (!tableByPartCode.TryAdd(partCode, t))
                throw new NgbConfigurationViolationException($"Catalog '{model.Meta.CatalogCode}' has duplicate part code '{partCode}'.");
        }

        foreach (var key in parts.Keys)
        {
            if (!tableByPartCode.ContainsKey(key))
                throw new NgbArgumentInvalidException(key,
                    $"Part '{GetPartLabel(key)}' is not available on this form.");
        }

        var tablesToWrite = new List<CatalogTableMetadata>();
        var rowsByTable = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (partCode, partPayload) in parts)
        {
            var table = tableByPartCode[partCode];
            tablesToWrite.Add(table);

            var known = table.Columns
                .Where(c => !IsCatalogId(c.ColumnName) && c.ColumnType != ColumnType.Json)
                .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

            var required = table.Columns
                .Where(c => !IsCatalogId(c.ColumnName) && c.ColumnType != ColumnType.Json && c.Required)
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
                    if (IsCatalogId(key))
                        throw new NgbArgumentInvalidException($"{rowPath}.catalog_id",
                            $"Catalog Id is managed automatically and cannot be set in {partLabel} row {rowNumber}.");

                    if (!known.ContainsKey(key))
                        throw new NgbArgumentInvalidException($"{rowPath}.{key}",
                            $"Field '{ToLabel(key, ColumnType.String)}' is not available in {partLabel} row {rowNumber}.");
                }

                foreach (var col in required)
                {
                    var fieldPath = $"{rowPath}.{col.ColumnName}";
                    if (!row.TryGetValue(col.ColumnName, out var el))
                        throw new NgbArgumentInvalidException(fieldPath,
                            $"{GetLabel(col)} is required in {partLabel} row {rowNumber}.");

                    var val = ConvertJsonValue(el, col.ColumnType, fieldPath, GetLabel(col));
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
                    var val = ConvertJsonValue(el, col.ColumnType, fieldPath, GetLabel(col));

                    if (col.Required && val is null)
                        throw new NgbArgumentInvalidException(fieldPath,
                            $"{GetLabel(col)} is required in {partLabel} row {rowNumber}.");

                    typed[name] = val;
                }

                typedRows.Add(typed);
            }

            rowsByTable[table.TableName] = typedRows;
        }

        return (tablesToWrite, rowsByTable);
    }

    private static CatalogQuery BuildQuery(
        CatalogModel model,
        string? search,
        IReadOnlyDictionary<string, string>? filters)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search;

        var allowed = model.ScalarColumns
            .ToDictionary(x => x.ColumnName, x => x, StringComparer.OrdinalIgnoreCase);

        var list = new List<CatalogFilter>();

        if (filters is not null && filters.Count > 0)
        {
            var ordered = filters
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (key, value) in ordered)
            {
                if (!allowed.ContainsKey(key))
                    throw new NgbArgumentInvalidException(nameof(filters),
                        $"Filter '{ToUserFilterLabel(key)}' is not available for this list.");

                list.Add(new CatalogFilter(key, value));
            }
        }

        return new CatalogQuery(normalizedSearch, list);
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

    private static string ToUserFilterLabel(string key)
    {
        var normalized = NormalizeFilterKey(key);
        return normalized switch
        {
            "deleted" or "trash" => "Deleted",
            _ => ValidationMessageFormatter.ToLabel(normalized, ColumnType.String)
        };
    }

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

        throw new NgbArgumentInvalidException(keyName, "Select Active, Deleted, or All.");
    }

    private static IReadOnlyList<CatalogHeadValue> ParseAndValidateFields(
        CatalogModel model,
        RecordPayload payload,
        bool requireAllRequired)
    {
        var fields = payload.Fields ?? new Dictionary<string, JsonElement>();

        // Validate unknown keys.
        var known = model.ScalarColumns
            .ToDictionary(x => x.ColumnName, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var key in fields.Keys)
        {
            if (!known.ContainsKey(key))
                throw new NgbArgumentInvalidException(nameof(payload),
                    $"Field '{ValidationMessageFormatter.ToLabel(key, ColumnType.String)}' is not available on this form.");
        }

        var result = new List<CatalogHeadValue>();

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
                col.ColumnType,
                $"payload.Fields.{col.ColumnName}",
                GetLabel(col));

            if (requireAllRequired && col.Required && value is null)
                throw new NgbArgumentInvalidException($"payload.Fields.{col.ColumnName}",
                    ValidationMessageFormatter.RequiredFieldMessage(GetLabel(col)));

            result.Add(new CatalogHeadValue(col.ColumnName, col.ColumnType, value));
        }

        return result;
    }

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

    private static DateTime ParseUtc(JsonElement el, string name)
    {
        var s = el.GetString() ?? el.ToString();
        var dt = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        dt.EnsureUtc(name);
        return dt;
    }

    private static decimal ParseDecimalInvariantStrict(string s)
    {
        // Strict parsing for user-provided string values:
        // - InvariantCulture
        // - no thousands separators
        // - '.' is the only decimal separator
        // This keeps API behavior deterministic across machines/locales and avoids accepting "12,34".
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

    private static CatalogItemDto ToItemDto(
        CatalogModel model,
        CatalogHeadRow row,
        IReadOnlyDictionary<string, RecordPartPayload>? parts)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in model.ScalarColumns)
        {
            row.Fields.TryGetValue(col.ColumnName, out var value);
            fields[col.ColumnName] = JsonTools.J(value);
        }

        var payload = new RecordPayload(fields, parts);

        return new CatalogItemDto(
            Id: row.Id,
            Display: row.Display,
            Payload: payload,
            IsMarkedForDeletion: row.IsMarkedForDeletion,
            IsDeleted: false);
    }

    private static CatalogTypeMetadataDto ToDto(CatalogTypeMetadata meta)
    {
        var head = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head);
        var columns = head?.Columns
            .Where(x => !string.Equals(x.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        var listCols = columns
            .Where(x => x.ColumnType != ColumnType.Json)
            .Take(6)
            .Select(c => new ColumnMetadataDto(
                Key: c.ColumnName,
                Label: GetLabel(c),
                DataType: ToDataType(c.ColumnType),
                Lookup: ToLookupDto(c.Lookup),
                Options: ToOptionsDto(c.Options)))
            .ToList();

        var formFields = columns
            .Where(x => x.ColumnType != ColumnType.Json)
            .Select(c => new FieldMetadataDto(
                Key: c.ColumnName,
                Label: GetLabel(c),
                DataType: ToDataType(c.ColumnType),
                UiControl: ToUiControl(c.ColumnType),
                IsRequired: c.Required,
                IsReadOnly: meta.Presentation.ComputedDisplay && string.Equals(c.ColumnName, "display", StringComparison.OrdinalIgnoreCase),
                Lookup: ToLookupDto(c.Lookup),
                Validation: c.MaxLength.HasValue ? new FieldValidationDto(MaxLength: c.MaxLength) : null,
                Options: ToOptionsDto(c.Options)))
            .Select(f => new FormRowDto([f]))
            .ToList();

        var parts = meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .Select(t =>
            {
                var partCode = t.GetRequiredPartCode(meta.CatalogCode);
                var partColumns = t.Columns
                    .Where(c => !IsCatalogId(c.ColumnName) && c.ColumnType != ColumnType.Json)
                    .ToList();

                var partListCols = partColumns
                    .Select(c => new ColumnMetadataDto(
                        Key: c.ColumnName,
                        Label: GetLabel(c),
                        DataType: ToDataType(c.ColumnType),
                        Lookup: ToLookupDto(c.Lookup),
                        Options: ToOptionsDto(c.Options)))
                    .ToList();

                return new PartMetadataDto(
                    PartCode: partCode,
                    Title: ToTitle(partCode),
                    List: new ListMetadataDto(partListCols));
            })
            .ToList();

        return new CatalogTypeMetadataDto(
            CatalogType: meta.CatalogCode,
            DisplayName: meta.DisplayName,
            Kind: EntityKind.Catalog,
            Icon: null,
            List: new ListMetadataDto(listCols),
            Form: new FormMetadataDto([new FormSectionDto("Main", formFields)]),
            Parts: parts.Count == 0 ? null : parts,
            Capabilities: new CatalogCapabilitiesDto());
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

    private static string ToTitle(string key)
        => ValidationMessageFormatter.ToLabel(key, ColumnType.String);

    private static string GetLabel(CatalogColumnMetadata column)
        => string.IsNullOrWhiteSpace(column.UiLabel)
            ? ToLabel(column.ColumnName, column.ColumnType)
            : column.UiLabel!;

    private static string GetPartLabel(string partCode)
        => ToLabel(partCode, ColumnType.String);

    private static bool IsCatalogId(string name)
        => string.Equals(name, "catalog_id", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, RecordPartPayload>?> ReadPartsAsync(
        CatalogModel model,
        Guid catalogId,
        CancellationToken ct)
    {
        var partTables = model.Meta.Tables
            .Where(t => t.Kind == TableKind.Part)
            .ToList();

        if (partTables.Count == 0)
            return null;

        var rowsByTable = await partsReader.GetPartsAsync(partTables, catalogId, ct);

        var parts = new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in partTables)
        {
            var partCode = t.GetRequiredPartCode(model.Meta.CatalogCode);
            rowsByTable.TryGetValue(t.TableName, out var rows);
            rows ??= Array.Empty<IReadOnlyDictionary<string, object?>>();

            var partRows = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Count);
            foreach (var r in rows)
            {
                var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in t.Columns)
                {
                    if (IsCatalogId(c.ColumnName) || c.ColumnType == ColumnType.Json)
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

    private sealed record CatalogModel(
        CatalogTypeMetadata Meta,
        IReadOnlyList<CatalogColumnMetadata> ScalarColumns,
        CatalogHeadDescriptor Head);
}
