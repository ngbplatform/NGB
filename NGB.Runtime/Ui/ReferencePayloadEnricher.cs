using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Readers.Accounts;
using NGB.Tools;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Ui;

/// <summary>
/// Server-side enrichment for reference fields in <see cref="RecordPayload"/>.
///
/// Goal:
/// - API responses should return reference values as <c>{ id, display }</c>
/// - API requests may still send plain GUIDs (preferred for write), but the backend
///   tolerates <c>{ id, display }</c> for compatibility (see ParseGuidOrRef).
///
/// Heuristics:
/// - *_account_id => Chart of Accounts
/// - *_register_id => Operational Register
/// - *_id => try resolve to catalog type by last segment (e.g. party_id => *.party)
/// - *_id => try resolve to document type by last segment (e.g. lease_id => *.lease)
///
/// If a display value cannot be resolved, a source-specific fallback is used.
/// </summary>
public interface IReferencePayloadEnricher
{
    Task<IReadOnlyList<CatalogItemDto>> EnrichCatalogItemsAsync(
        CatalogHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<CatalogItemDto> items,
        CancellationToken ct);

    Task<IReadOnlyList<DocumentDto>> EnrichDocumentItemsAsync(
        DocumentHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<DocumentDto> items,
        CancellationToken ct);
}

public sealed class ReferencePayloadEnricher(
    ICatalogTypeRegistry catalogTypes,
    IDocumentTypeRegistry documentTypes,
    ICatalogEnrichmentReader catalogEnrichmentReader,
    IDocumentDisplayReader documentDisplayReader,
    IAccountLookupReader accountLookupReader,
    IOperationalRegisterRepository opregRepo)
    : IReferencePayloadEnricher
{
    private sealed record RefSource(RefKind Kind, IReadOnlyList<string>? TypeCodes);

    private sealed record EnrichmentColumn(string Key, ColumnType Type, LookupSourceMetadata? Lookup = null);

    private sealed record EnrichmentField(string FieldKey, RefSource Source);

    private sealed record PayloadEnrichmentPlan(
        IReadOnlyList<EnrichmentField> HeadFields,
        IReadOnlyDictionary<string, IReadOnlyList<EnrichmentField>> PartFields)
    {
        public bool HasWork => HeadFields.Count > 0 || PartFields.Count > 0;
    }

    private enum RefKind
    {
        Catalog = 1,
        Document = 2,
        ChartOfAccounts = 3,
        OperationalRegister = 4
    }

    public async Task<IReadOnlyList<CatalogItemDto>> EnrichCatalogItemsAsync(
        CatalogHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<CatalogItemDto> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var plan = BuildCatalogPlan(ownerHead, ownerTypeCode);
        if (!plan.HasWork)
            return items;

        var enriched = await EnrichPayloadsAsync(plan, items.Select(i => i.Payload).ToList(), ct);
        return items.Select((it, i) => it with { Payload = enriched[i] }).ToList();
    }

    public async Task<IReadOnlyList<DocumentDto>> EnrichDocumentItemsAsync(
        DocumentHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<DocumentDto> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var plan = BuildDocumentPlan(ownerHead, ownerTypeCode);
        if (!plan.HasWork)
            return items;

        var enriched = await EnrichPayloadsAsync(plan, items.Select(i => i.Payload).ToList(), ct);
        return items.Select((it, i) => it with { Payload = enriched[i] }).ToList();
    }

    private PayloadEnrichmentPlan BuildCatalogPlan(CatalogHeadDescriptor ownerHead, string ownerTypeCode)
    {
        catalogTypes.TryGet(ownerTypeCode, out var catalogMeta);
        var headColumns = BuildCatalogHeadColumns(ownerHead, catalogMeta);
        var partColumns = catalogMeta is null
            ? new Dictionary<string, IReadOnlyList<EnrichmentColumn>>(StringComparer.OrdinalIgnoreCase)
            : BuildCatalogPartColumns(catalogMeta);

        return BuildPlan(headColumns, partColumns);
    }

    private PayloadEnrichmentPlan BuildDocumentPlan(DocumentHeadDescriptor ownerHead, string ownerTypeCode)
    {
        var documentMeta = documentTypes.TryGet(ownerTypeCode);
        var headColumns = BuildDocumentHeadColumns(ownerHead, documentMeta);
        var partColumns = documentMeta is null
            ? new Dictionary<string, IReadOnlyList<EnrichmentColumn>>(StringComparer.OrdinalIgnoreCase)
            : BuildDocumentPartColumns(documentMeta);

        return BuildPlan(headColumns, partColumns);
    }

    private PayloadEnrichmentPlan BuildPlan(
        IReadOnlyList<EnrichmentColumn> headColumns,
        IReadOnlyDictionary<string, IReadOnlyList<EnrichmentColumn>> partColumns)
    {
        var catalogCodes = catalogTypes.All()
            .Select(x => x.CatalogCode)
            .ToArray();
        var documentCodes = documentTypes.GetAll()
            .Select(x => x.TypeCode)
            .ToArray();

        var headFields = BuildFieldSources(headColumns, catalogCodes, documentCodes);
        var partFields = new Dictionary<string, IReadOnlyList<EnrichmentField>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (partCode, columns) in partColumns)
        {
            var fields = BuildFieldSources(columns, catalogCodes, documentCodes);
            if (fields.Count > 0)
                partFields[partCode] = fields;
        }

        return new PayloadEnrichmentPlan(headFields, partFields);
    }

    private static IReadOnlyList<EnrichmentColumn> BuildCatalogHeadColumns(
        CatalogHeadDescriptor ownerHead,
        CatalogTypeMetadata? catalogMeta)
    {
        var lookupByName = catalogMeta?.Tables
            .FirstOrDefault(x => x.Kind == TableKind.Head)?
            .Columns
            .ToDictionary(x => x.ColumnName, x => x.Lookup, StringComparer.OrdinalIgnoreCase);

        return ownerHead.Columns
            .Select(c =>
            {
                LookupSourceMetadata? lookup = null;
                if (lookupByName is not null && lookupByName.TryGetValue(c.ColumnName, out var resolvedLookup))
                    lookup = resolvedLookup;
                return new EnrichmentColumn(c.ColumnName, c.ColumnType, lookup);
            })
            .ToList();
    }

    private static IReadOnlyList<EnrichmentColumn> BuildDocumentHeadColumns(
        DocumentHeadDescriptor ownerHead,
        DocumentTypeMetadata? documentMeta)
    {
        var lookupByName = documentMeta?.Tables
            .FirstOrDefault(x => x.Kind == TableKind.Head)?
            .Columns
            .ToDictionary(x => x.ColumnName, x => x.Lookup, StringComparer.OrdinalIgnoreCase);

        return ownerHead.Columns
            .Select(c =>
            {
                LookupSourceMetadata? lookup = null;
                if (lookupByName is not null && lookupByName.TryGetValue(c.ColumnName, out var resolvedLookup))
                    lookup = resolvedLookup;
                return new EnrichmentColumn(c.ColumnName, c.ColumnType, lookup);
            })
            .ToList();
    }

    private static IReadOnlyList<EnrichmentField> BuildFieldSources(
        IReadOnlyList<EnrichmentColumn> columns,
        IReadOnlyCollection<string> catalogCodes,
        IReadOnlyCollection<string> documentCodes)
    {
        var result = new List<EnrichmentField>();

        foreach (var column in columns)
        {
            if (column.Type != ColumnType.Guid || !IsCandidateRefField(column.Key))
                continue;

            if (!TryResolveSource(column.Key, column.Lookup, catalogCodes, documentCodes, out var source))
                continue;

            result.Add(new EnrichmentField(column.Key, source));
        }

        return result
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, IReadOnlyList<EnrichmentColumn>> BuildCatalogPartColumns(
        CatalogTypeMetadata meta)
    {
        var dict = new Dictionary<string, IReadOnlyList<EnrichmentColumn>>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in meta.Tables.Where(x => x.Kind == TableKind.Part))
        {
            var code = t.GetRequiredPartCode(meta.CatalogCode);
            if (string.IsNullOrWhiteSpace(code))
                continue;

            dict[code] = t.Columns
                .Where(c => !string.Equals(c.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase) && c.ColumnType != ColumnType.Json)
                .Select(c => new EnrichmentColumn(c.ColumnName, c.ColumnType, c.Lookup))
                .ToList();
        }

        return dict;
    }

    private static Dictionary<string, IReadOnlyList<EnrichmentColumn>> BuildDocumentPartColumns(DocumentTypeMetadata meta)
    {
        var dict = new Dictionary<string, IReadOnlyList<EnrichmentColumn>>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in meta.Tables.Where(x => x.Kind == TableKind.Part))
        {
            var code = t.GetRequiredPartCode(meta.TypeCode);
            if (string.IsNullOrWhiteSpace(code))
                continue;

            dict[code] = t.Columns
                .Where(c => !string.Equals(c.ColumnName, "document_id", StringComparison.OrdinalIgnoreCase) && c.Type != ColumnType.Json)
                .Select(c => new EnrichmentColumn(c.ColumnName, c.Type, c.Lookup))
                .ToList();
        }

        return dict;
    }

    private async Task<IReadOnlyList<RecordPayload>> EnrichPayloadsAsync(
        PayloadEnrichmentPlan plan,
        IReadOnlyList<RecordPayload> payloads,
        CancellationToken ct)
    {
        if (!plan.HasWork)
            return payloads;

        var coaIds = new HashSet<Guid>();
        var opregIds = new HashSet<Guid>();
        var catalogTypeToIds = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        var documentIds = new HashSet<Guid>();

        foreach (var payload in payloads)
        {
            CollectIds(payload.Fields, plan.HeadFields, coaIds, opregIds, catalogTypeToIds, documentIds);

            if (payload.Parts is null || payload.Parts.Count == 0 || plan.PartFields.Count == 0)
                continue;

            foreach (var (partCode, part) in payload.Parts)
            {
                if (!plan.PartFields.TryGetValue(partCode, out var fields))
                    continue;

                foreach (var row in part.Rows)
                {
                    CollectIds(row, fields, coaIds, opregIds, catalogTypeToIds, documentIds);
                }
            }
        }

        var coaLabels = await ResolveChartOfAccountsAsync(coaIds, ct);
        var opregLabels = await ResolveOperationalRegistersAsync(opregIds, ct);
        var catalogLabelsByType = catalogTypeToIds.Count == 0
            ? new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase)
            : await catalogEnrichmentReader.ResolveManyAsync(
                catalogTypeToIds.ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyCollection<Guid>)x.Value,
                    StringComparer.OrdinalIgnoreCase),
                ct);
        var documentLabels = await ResolveDocumentsAsync(documentIds, ct);

        var result = new List<RecordPayload>(payloads.Count);

        foreach (var payload in payloads)
        {
            Dictionary<string, JsonElement>? fields = null;
            if (payload.Fields is not null && payload.Fields.Count > 0)
            {
                foreach (var field in plan.HeadFields)
                {
                    if (!payload.Fields.TryGetValue(field.FieldKey, out var el))
                        continue;

                    if (!TryExtractGuidId(el, out var id))
                        continue;

                    fields ??= new Dictionary<string, JsonElement>(payload.Fields, StringComparer.OrdinalIgnoreCase);
                    var display = ResolveDisplay(field.Source, id, coaLabels, opregLabels, catalogLabelsByType, documentLabels);
                    fields[field.FieldKey] = JsonTools.Jobj(new RefValueDto(id, display));
                }
            }

            Dictionary<string, RecordPartPayload>? parts = null;
            if (payload.Parts is not null && payload.Parts.Count > 0 && plan.PartFields.Count > 0)
            {
                foreach (var (partCode, part) in payload.Parts)
                {
                    if (!plan.PartFields.TryGetValue(partCode, out var partFields) || partFields.Count == 0)
                        continue;

                    List<IReadOnlyDictionary<string, JsonElement>>? rows = null;

                    for (var rowIndex = 0; rowIndex < part.Rows.Count; rowIndex++)
                    {
                        var row = part.Rows[rowIndex];
                        Dictionary<string, JsonElement>? rowFields = null;

                        foreach (var field in partFields)
                        {
                            if (!row.TryGetValue(field.FieldKey, out var el))
                                continue;

                            if (!TryExtractGuidId(el, out var id))
                                continue;

                            rowFields ??= new Dictionary<string, JsonElement>(row, StringComparer.OrdinalIgnoreCase);
                            var display = ResolveDisplay(field.Source, id, coaLabels, opregLabels, catalogLabelsByType, documentLabels);
                            rowFields[field.FieldKey] = JsonTools.Jobj(new RefValueDto(id, display));
                        }

                        if (rowFields is null)
                            continue;

                        rows ??= part.Rows.ToList();
                        rows[rowIndex] = rowFields;
                    }

                    if (rows is null)
                        continue;

                    parts ??= new Dictionary<string, RecordPartPayload>(payload.Parts, StringComparer.OrdinalIgnoreCase);
                    parts[partCode] = new RecordPartPayload(rows);
                }
            }

            result.Add(fields is null && parts is null
                ? payload
                : new RecordPayload(Fields: fields ?? payload.Fields, Parts: parts ?? payload.Parts));
        }

        return result;
    }

    private static void CollectIds(
        IReadOnlyDictionary<string, JsonElement>? values,
        IReadOnlyList<EnrichmentField> fields,
        HashSet<Guid> coaIds,
        HashSet<Guid> opregIds,
        IDictionary<string, HashSet<Guid>> catalogTypeToIds,
        ISet<Guid> documentIds)
    {
        if (values is null || values.Count == 0 || fields.Count == 0)
            return;

        foreach (var field in fields)
        {
            if (!values.TryGetValue(field.FieldKey, out var el))
                continue;

            if (!TryExtractGuidId(el, out var id))
                continue;

            switch (field.Source.Kind)
            {
                case RefKind.ChartOfAccounts:
                    coaIds.Add(id);
                    break;
                case RefKind.OperationalRegister:
                    opregIds.Add(id);
                    break;
                case RefKind.Catalog:
                    if (field.Source.TypeCodes is null)
                        break;

                    foreach (var typeCode in field.Source.TypeCodes)
                    {
                        if (!catalogTypeToIds.TryGetValue(typeCode, out var set))
                            catalogTypeToIds[typeCode] = set = [];

                        set.Add(id);
                    }

                    break;
                case RefKind.Document:
                    documentIds.Add(id);
                    break;
            }
        }
    }

    private static bool IsCandidateRefField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        // Exclude common technical columns.
        if (string.Equals(key, "catalog_id", StringComparison.OrdinalIgnoreCase))
            return false;
        
        if (string.Equals(key, "document_id", StringComparison.OrdinalIgnoreCase))
            return false;

        // We enrich based on naming convention.
        return key.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveSource(
        string fieldKey,
        LookupSourceMetadata? explicitLookup,
        IReadOnlyCollection<string> catalogCodes,
        IReadOnlyCollection<string> documentCodes,
        out RefSource source)
    {
        source = null!;

        if (explicitLookup is not null)
        {
            source = ToRefSource(explicitLookup);
            return true;
        }

        if (fieldKey.EndsWith("_account_id", StringComparison.OrdinalIgnoreCase))
        {
            source = new RefSource(RefKind.ChartOfAccounts, null);
            return true;
        }

        if (fieldKey.EndsWith("_register_id", StringComparison.OrdinalIgnoreCase))
        {
            source = new RefSource(RefKind.OperationalRegister, null);
            return true;
        }

        if (!fieldKey.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
            return false;

        var tail = fieldKey[..^3]; // remove _id
        if (string.IsNullOrWhiteSpace(tail))
            return false;

        // Prefer catalogs.
        var catalogType = TryMatchByLastSegment(catalogCodes, tail);
        if (catalogType is not null)
        {
            source = new RefSource(RefKind.Catalog, [catalogType]);
            return true;
        }

        // Then documents.
        var docTypes = TryMatchDocumentCodes(documentCodes, tail);
        if (docTypes.Count > 0)
        {
            source = new RefSource(RefKind.Document, docTypes);
            return true;
        }

        return false;
    }

    private static RefSource ToRefSource(LookupSourceMetadata lookup)
        => lookup switch
        {
            CatalogLookupSourceMetadata catalog => new RefSource(RefKind.Catalog, [catalog.CatalogType]),
            DocumentLookupSourceMetadata document => new RefSource(RefKind.Document, document.DocumentTypes),
            ChartOfAccountsLookupSourceMetadata => new RefSource(RefKind.ChartOfAccounts, null),
            _ => throw new NgbConfigurationViolationException($"Unsupported lookup source metadata type '{lookup.GetType().Name}'.")
        };

    private static string? TryMatchByLastSegment(IEnumerable<string> codes, string segment)
    {
        var matches = codes
            .Where(c => c.EndsWith("." + segment, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c, segment, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<string> TryMatchDocumentCodes(IEnumerable<string> codes, string segment)
    {
        var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            segment
        };

        var lastUnderscore = segment.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore + 1 < segment.Length)
            segments.Add(segment[(lastUnderscore + 1)..]);

        return codes
            .Where(c =>
            {
                var suffix = c[(c.LastIndexOf('.') + 1)..];
                return segments.Any(s =>
                    string.Equals(suffix, s, StringComparison.OrdinalIgnoreCase)
                    || suffix.EndsWith("_" + s, StringComparison.OrdinalIgnoreCase));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryExtractGuidId(JsonElement el, out Guid id)
    {
        id = Guid.Empty;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return Guid.TryParse(s, out id);
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                var s = idEl.GetString();
                return Guid.TryParse(s, out id);
            }

            // tolerate PascalCase too
            if (el.TryGetProperty("Id", out var idEl2) && idEl2.ValueKind == JsonValueKind.String)
            {
                var s = idEl2.GetString();
                return Guid.TryParse(s, out id);
            }
        }

        return false;
    }

    private static string ResolveDisplay(
        RefSource source,
        Guid id,
        IReadOnlyDictionary<Guid, string> coaLabels,
        IReadOnlyDictionary<Guid, string> opregLabels,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> catalogLabelsByType,
        IReadOnlyDictionary<Guid, string> documentLabels)
    {
        return source.Kind switch
        {
            RefKind.ChartOfAccounts => coaLabels.TryGetValue(id, out var c) ? c : id.ToString(),
            RefKind.OperationalRegister => opregLabels.TryGetValue(id, out var r) ? r : id.ToString(),
            RefKind.Catalog when source.TypeCodes is not null && source.TypeCodes.Count > 0
                => ResolveFromAny(source.TypeCodes, id, catalogLabelsByType),
            RefKind.Document => documentLabels.TryGetValue(id, out var display) ? display : id.ToString(),
            _ => id.ToString()
        };
    }

    private static string ResolveFromAny(
        IReadOnlyList<string> typeCodes,
        Guid id,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> labelsByType)
    {
        var fallback = id.ToString();
        foreach (var typeCode in typeCodes)
        {
            if (!labelsByType.TryGetValue(typeCode, out var labels))
                continue;

            if (!labels.TryGetValue(id, out var display))
                continue;

            if (string.IsNullOrWhiteSpace(display) || string.Equals(display, fallback, StringComparison.OrdinalIgnoreCase))
                continue;

            return display;
        }

        return fallback;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveDocumentsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var refs = await documentDisplayReader.ResolveRefsAsync(ids, ct);
        return refs
            .Where(x => !string.IsNullOrWhiteSpace(x.Value.TypeCode))
            .ToDictionary(x => x.Key, x => x.Value.Display);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveChartOfAccountsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await accountLookupReader.GetByIdsAsync(ids, ct);
        var dict = new Dictionary<Guid, string>(ids.Count);

        foreach (var row in rows)
        {
            dict[row.AccountId] = string.IsNullOrWhiteSpace(row.Code)
                ? row.Name
                : $"{row.Code} — {row.Name}";
        }

        foreach (var id in ids)
        {
            dict.TryAdd(id, id.ToString());
        }

        return dict;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveOperationalRegistersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await opregRepo.GetByIdsAsync(ids, ct);
        var dict = new Dictionary<Guid, string>(ids.Count);

        foreach (var row in rows)
        {
            dict[row.RegisterId] = string.IsNullOrWhiteSpace(row.Code)
                ? row.Name
                : $"{row.Code} — {row.Name}";
        }

        foreach (var id in ids)
        {
            dict.TryAdd(id, id.ToString());
        }

        return dict;
    }
}
