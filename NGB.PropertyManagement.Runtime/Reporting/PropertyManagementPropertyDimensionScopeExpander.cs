using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Core.Reporting;
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
        AccountingReportCodes.TrialBalance,
        AccountingReportCodes.BalanceSheet,
        AccountingReportCodes.IncomeStatement,
        AccountingReportCodes.GeneralJournal,
        AccountingReportCodes.AccountCard,
        AccountingReportCodes.GeneralLedgerAggregated
    };

    private static readonly Guid PropertyDimensionId = DeterministicGuid.Create(
        $"Dimension|{CodeNormalizer.NormalizeCodeNorm(PropertyManagementCodes.Property, nameof(PropertyManagementCodes.Property))}");

    private CatalogHeadDescriptor? _head;

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
        var selectedIds = scope.ValueIds.Distinct().ToArray();
        var selectedRows = await reader.GetByIdsWithFieldsAsync(GetHead(), selectedIds, ct);
        var rowsById = selectedRows.ToDictionary(static row => row.Id);
        var missingIds = selectedIds.Where(id => !rowsById.ContainsKey(id)).ToArray();
        var missingCatalogs = missingIds.Length == 0
            ? new Dictionary<Guid, NGB.Core.Catalogs.CatalogRecord>()
            : await catalogs.GetByIdsAsync(missingIds, ct);
        var ids = new SortedSet<Guid>();
        var buildingIds = new List<Guid>();

        foreach (var propertyId in selectedIds)
        {
            var propertyRow = rowsById.TryGetValue(propertyId, out var row)
                ? row
                : ThrowInvalidProperty(propertyId, missingCatalogs);

            if (propertyRow.IsMarkedForDeletion)
                throw new NgbArgumentInvalidException(FilterParameterName, "Selected property is deleted.");

            ids.Add(propertyId);

            var kind = NormalizeKind(ReadString(propertyRow.Fields, "kind"));
            if (string.Equals(kind, "Building", StringComparison.Ordinal))
                buildingIds.Add(propertyId);
        }

        if (buildingIds.Count > 0)
        {
            var descendants = await reader.GetActiveDescendantIdsAsync(
                GetHead(),
                buildingIds,
                "parent_property_id",
                ct);
            ids.UnionWith(descendants);
        }

        // Effective scope is already expanded, so the descendants flag is consumed here.
        return new DimensionScope(scope.DimensionId, ids, includeDescendants: false);
    }

    private static CatalogHeadRow ThrowInvalidProperty(
        Guid propertyId,
        IReadOnlyDictionary<Guid, NGB.Core.Catalogs.CatalogRecord> catalogs)
    {
        if (!catalogs.TryGetValue(propertyId, out var catalog))
            throw new NgbArgumentInvalidException(FilterParameterName, "Selected property was not found.");

        if (!string.Equals(catalog.CatalogCode, PropertyManagementCodes.Property, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(FilterParameterName, "Select a valid Property.");

        if (catalog.IsDeleted)
            throw new NgbArgumentInvalidException(FilterParameterName, "Selected property is deleted.");

        throw new NgbConfigurationViolationException(
            "Selected property data is incomplete.",
            context: new Dictionary<string, object?>
            {
                ["catalogType"] = PropertyManagementCodes.Property,
                ["propertyId"] = propertyId,
                ["filter"] = FilterParameterName
            });
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
}
