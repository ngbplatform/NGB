using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;

namespace NGB.PropertyManagement.Runtime.Reporting;

/// <summary>
/// Expands pm.property dimension scopes for accounting reports.
/// If a selected value is a Building and IncludeDescendants=true, effective scope becomes:
///   Building + all active descendant properties.
/// UI keeps the user's original selection; readers will later consume the expanded effective scope.
/// </summary>
public sealed class PropertyManagementPropertyDimensionScopeExpander(
    ICatalogTypeRegistry catalogTypes,
    ICatalogRepository catalogs,
    ICatalogReader reader)
    : IReportDimensionScopeExpander
{
    private const string FilterParameterName = "dimensionScopes";

    private static readonly HashSet<string> SupportedReportCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounting.trial_balance",
        "accounting.balance_sheet",
        "accounting.income_statement",
        "accounting.general_journal",
        "accounting.account_card",
        "accounting.general_ledger_aggregated"
    };

    private static readonly Guid PropertyDimensionId = DeterministicGuid.Create(
        $"Dimension|{CodeNormalizer.NormalizeCodeNorm(PropertyManagementCodes.Property, nameof(PropertyManagementCodes.Property))}");

    private CatalogHeadDescriptor? _head;
    private Task<PropertyHierarchySnapshot>? _activeHierarchySnapshotTask;

    public async Task<DimensionScopeBag> ExpandAsync(string reportCode, DimensionScopeBag scopes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCode))
            throw new NgbArgumentRequiredException(nameof(reportCode));

        if (scopes is null)
            throw new NgbArgumentRequiredException(nameof(scopes));

        if (scopes.IsEmpty || !SupportedReportCodes.Contains(reportCode))
            return scopes;

        var changed = false;
        var effective = new List<DimensionScope>(scopes.Count);

        foreach (var scope in scopes)
        {
            if (scope.DimensionId != PropertyDimensionId || !scope.IncludeDescendants)
            {
                effective.Add(scope);
                continue;
            }

            var expanded = await ExpandPropertyScopeAsync(scope, ct);
            changed = true;
            effective.Add(expanded);
        }

        return changed ? new DimensionScopeBag(effective) : scopes;
    }

    private async Task<DimensionScope> ExpandPropertyScopeAsync(DimensionScope scope, CancellationToken ct)
    {
        var hierarchy = await GetActiveHierarchySnapshotAsync(ct);
        var ids = new SortedSet<Guid>();

        foreach (var propertyId in scope.ValueIds)
        {
            var propertyRow = await GetActivePropertyRowAsync(propertyId, hierarchy, ct);
            ids.Add(propertyId);

            var kind = NormalizeKind(ReadString(propertyRow.Fields, "kind"));
            if (string.Equals(kind, "Building", StringComparison.Ordinal))
                hierarchy.AddDescendants(propertyId, ids);
        }

        // Effective scope is already expanded, so the descendants flag is consumed here.
        return new DimensionScope(scope.DimensionId, ids, includeDescendants: false);
    }

    private async Task<PropertyHierarchySnapshot> GetActiveHierarchySnapshotAsync(CancellationToken ct)
    {
        if (_activeHierarchySnapshotTask is not null)
            return await _activeHierarchySnapshotTask;

        _activeHierarchySnapshotTask = LoadActiveHierarchySnapshotAsync(ct);
        return await _activeHierarchySnapshotTask;
    }

    private async Task<PropertyHierarchySnapshot> LoadActiveHierarchySnapshotAsync(CancellationToken ct)
    {
        var rowsById = new Dictionary<Guid, CatalogHeadRow>();
        var childrenByParentId = new Dictionary<Guid, List<Guid>>();
        var query = new CatalogQuery(Search: null, Filters: [])
        {
            SoftDeleteFilterMode = SoftDeleteFilterMode.Active
        };

        const int limit = 512;

        for (var offset = 0; ; offset += limit)
        {
            var rows = await reader.GetPageAsync(GetHead(), query, offset, limit, ct);
            if (rows.Count == 0)
                break;

            foreach (var row in rows)
            {
                rowsById[row.Id] = row;

                if (TryReadGuid(row.Fields, "parent_property_id", out var parentPropertyId)
                    && parentPropertyId != Guid.Empty)
                {
                    if (!childrenByParentId.TryGetValue(parentPropertyId, out var children))
                    {
                        children = [];
                        childrenByParentId[parentPropertyId] = children;
                    }

                    children.Add(row.Id);
                }
            }

            if (rows.Count < limit)
                break;
        }

        return new PropertyHierarchySnapshot(rowsById, childrenByParentId);
    }

    private async Task<CatalogHeadRow> GetActivePropertyRowAsync(
        Guid propertyId,
        PropertyHierarchySnapshot hierarchy,
        CancellationToken ct)
    {
        if (propertyId == Guid.Empty)
            throw new NgbArgumentInvalidException(FilterParameterName, "Select a valid Property.");

        if (hierarchy.RowsById.TryGetValue(propertyId, out var activeRow))
            return activeRow;

        var catalog = await catalogs.GetAsync(propertyId, ct);
        if (catalog is null)
            throw new NgbArgumentInvalidException(FilterParameterName, "Selected property was not found.");

        if (!string.Equals(catalog.CatalogCode, PropertyManagementCodes.Property, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(FilterParameterName, "Select a valid Property.");

        if (catalog.IsDeleted)
            throw new NgbArgumentInvalidException(FilterParameterName, "Selected property is deleted.");

        var row = await reader.GetByIdAsync(GetHead(), propertyId, ct);
        if (row is null)
        {
            throw new NgbConfigurationViolationException(
                "Selected property data is incomplete.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.Property,
                    ["propertyId"] = propertyId,
                    ["filter"] = FilterParameterName
                });
        }

        return row;
    }

    private CatalogHeadDescriptor GetHead()
    {
        if (_head is not null)
            return _head;

        var meta = catalogTypes.GetRequired(PropertyManagementCodes.Property);
        var headTable = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has no Head table metadata.");

        var displayColumn = meta.Presentation.DisplayColumn;
        if (string.IsNullOrWhiteSpace(displayColumn))
            throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has empty Presentation.DisplayColumn.");

        var scalarColumns = headTable.Columns
            .Where(x => !string.Equals(x.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _head = new CatalogHeadDescriptor(
            CatalogCode: meta.CatalogCode,
            HeadTableName: headTable.TableName,
            DisplayColumn: displayColumn,
            Columns: scalarColumns
                .Select(c => new CatalogHeadColumn(c.ColumnName, c.ColumnType))
                .ToList());

        return _head;
    }

    private static string? NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        if (string.Equals(kind, "Building", StringComparison.OrdinalIgnoreCase))
            return "Building";

        if (string.Equals(kind, "Unit", StringComparison.OrdinalIgnoreCase))
            return "Unit";

        return kind.Trim();
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields, string field)
    {
        if (!fields.TryGetValue(field, out var raw) || raw is null)
            return null;

        return raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement e => e.ToString(),
            _ => raw.ToString()
        };
    }

    private static bool TryReadGuid(IReadOnlyDictionary<string, object?> fields, string field, out Guid value)
    {
        if (!fields.TryGetValue(field, out var raw) || raw is null)
        {
            value = Guid.Empty;
            return false;
        }

        switch (raw)
        {
            case Guid guid when guid != Guid.Empty:
                value = guid;
                return true;
            case string s when Guid.TryParse(s, out var parsed) && parsed != Guid.Empty:
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } json
                when Guid.TryParse(json.GetString(), out var parsed) && parsed != Guid.Empty:
                value = parsed;
                return true;
            default:
                value = Guid.Empty;
                return false;
        }
    }

    private sealed class PropertyHierarchySnapshot(
        IReadOnlyDictionary<Guid, CatalogHeadRow> rowsById,
        IReadOnlyDictionary<Guid, List<Guid>> childrenByParentId)
    {
        public IReadOnlyDictionary<Guid, CatalogHeadRow> RowsById => rowsById;

        public void AddDescendants(Guid rootPropertyId, SortedSet<Guid> ids)
        {
            var queue = new Queue<Guid>();
            queue.Enqueue(rootPropertyId);

            while (queue.Count > 0)
            {
                var parentId = queue.Dequeue();
                if (!childrenByParentId.TryGetValue(parentId, out var children))
                    continue;

                foreach (var childId in children)
                {
                    if (!ids.Add(childId) || !rowsById.TryGetValue(childId, out var childRow))
                        continue;

                    var kind = NormalizeKind(ReadString(childRow.Fields, "kind"));
                    if (string.Equals(kind, "Building", StringComparison.Ordinal))
                        queue.Enqueue(childId);
                }
            }
        }
    }
}
