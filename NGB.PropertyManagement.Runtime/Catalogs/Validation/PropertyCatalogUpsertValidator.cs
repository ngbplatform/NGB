using System.Text.Json;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Catalogs.Validation;

/// <summary>
/// Runtime validation for pm.property:
/// - Property(kind=Building|Unit, parent_property_id, unit_no)
/// - conditional required/forbidden fields
/// - parent must be an existing non-deleted Building
/// - prevent parent cycles
/// - friendly duplicate unit_no errors (instead of DB constraint failure)
/// </summary>
public sealed class PropertyCatalogUpsertValidator(
    ICatalogTypeRegistry catalogTypes,
    ICatalogRepository catalogs,
    ICatalogReader reader)
    : ICatalogUpsertValidator
{
    public string TypeCode => PropertyManagementCodes.Property;

    private CatalogHeadDescriptor? _head;

    public async Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
            throw new NgbConfigurationViolationException(
                $"{nameof(PropertyCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");

        var kindRaw = ReadString(context.Fields, "kind");
        if (string.IsNullOrWhiteSpace(kindRaw))
            throw PropertyValidationException.KindRequired();

        var kind = NormalizeKind(kindRaw);
        if (kind is null)
            throw PropertyValidationException.KindInvalid(kindRaw);

        // Keep the model stable: kind is immutable after creation.
        if (!context.IsCreate)
        {
            var existing = await reader.GetByIdAsync(GetHead(), context.CatalogId, ct);
            if (existing is not null)
            {
                var oldKindRaw = ReadString(existing.Fields, "kind");
                var oldKind = string.IsNullOrWhiteSpace(oldKindRaw) ? null : NormalizeKind(oldKindRaw);
                
                if (oldKind is not null && !string.Equals(oldKind, kind, StringComparison.Ordinal))
                    throw PropertyValidationException.KindImmutable(context.CatalogId, oldKind, kind);
            }
        }

        if (string.Equals(kind, "Building", StringComparison.Ordinal))
        {
            ValidateBuilding(context);
            return;
        }

        await ValidateUnitAsync(context, ct);
    }

    private static void ValidateBuilding(CatalogUpsertValidationContext context)
    {
        var parent = ReadGuid(context.Fields, "parent_property_id");
        if (parent is not null)
            throw PropertyValidationException.BuildingCannotHaveParent(parent.Value);

        var unitNo = ReadString(context.Fields, "unit_no");
        if (!string.IsNullOrWhiteSpace(unitNo))
            throw PropertyValidationException.BuildingCannotHaveUnitNo(unitNo);

        RequireNonEmpty(context.Fields, "address_line1");
        RequireNonEmpty(context.Fields, "city");
        RequireNonEmpty(context.Fields, "state");
        RequireNonEmpty(context.Fields, "zip");
    }

    private async Task ValidateUnitAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        var parentId = ReadGuid(context.Fields, "parent_property_id");
        if (parentId is null)
            throw PropertyValidationException.UnitParentRequired();

        if (parentId.Value == context.CatalogId)
            throw PropertyValidationException.CycleDetected(context.CatalogId, parentId.Value);

        var unitNoRaw = ReadString(context.Fields, "unit_no");
        if (string.IsNullOrWhiteSpace(unitNoRaw))
            throw PropertyValidationException.UnitNoRequired();

        var unitNo = unitNoRaw.Trim();
        if (unitNo.Length == 0)
            throw PropertyValidationException.UnitNoRequired();

        if (!string.Equals(unitNoRaw, unitNo, StringComparison.Ordinal))
            throw PropertyValidationException.UnitNoInvalid(unitNoRaw);

        // Units must not carry address columns.
        EnsureNullOrEmpty(context.Fields, "address_line1");
        EnsureNullOrEmpty(context.Fields, "address_line2");
        EnsureNullOrEmpty(context.Fields, "city");
        EnsureNullOrEmpty(context.Fields, "state");
        EnsureNullOrEmpty(context.Fields, "zip");

        // Parent must exist and be a non-deleted pm.property catalog.
        var parentCatalog = await catalogs.GetAsync(parentId.Value, ct);
        if (parentCatalog is null)
            throw PropertyValidationException.ParentNotFound(parentId.Value);

        if (!string.Equals(parentCatalog.CatalogCode, TypeCode, StringComparison.OrdinalIgnoreCase))
            throw PropertyValidationException.ParentWrongCatalog(parentId.Value, parentCatalog.CatalogCode);

        if (parentCatalog.IsDeleted)
            throw PropertyValidationException.ParentDeleted(parentId.Value);

        // Parent must be a Building.
        var parentRow = await reader.GetByIdAsync(GetHead(), parentId.Value, ct);
        if (parentRow is null)
            throw PropertyValidationException.ParentNotFound(parentId.Value);

        var parentKindRaw = ReadString(parentRow.Fields, "kind");
        var parentKind = string.IsNullOrWhiteSpace(parentKindRaw) ? null : NormalizeKind(parentKindRaw);
        if (!string.Equals(parentKind, "Building", StringComparison.Ordinal))
            throw PropertyValidationException.ParentNotBuilding(parentId.Value, parentKindRaw);

        // Prevent cycles (defensive: even if the current model expects only Unit->Building).
        await EnsureNoParentCycleAsync(context.CatalogId, parentId.Value, ct);

        // Friendly duplicate check (active units only).
        await EnsureUnitNoUniqueAsync(context.CatalogId, parentId.Value, unitNo, ct);
    }

    private async Task EnsureUnitNoUniqueAsync(Guid catalogId, Guid buildingId, string unitNo, CancellationToken ct)
    {
        var query = new CatalogQuery(
            Search: null,
            Filters: new List<CatalogFilter>
            {
                new("kind", "Unit"),
                new("parent_property_id", buildingId.ToString()),
                new("unit_no", unitNo)
            })
        {
            SoftDeleteFilterMode = SoftDeleteFilterMode.Active
        };

        var rows = await reader.GetPageAsync(GetHead(), query, offset: 0, limit: 5, ct);
        if (rows.Any(r => r.Id != catalogId))
            throw new PropertyUnitNoDuplicateException(buildingId, unitNo);
    }

    private async Task EnsureNoParentCycleAsync(Guid catalogId, Guid parentId, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { catalogId };
        var cur = parentId;

        for (var i = 0; i < 32; i++)
        {
            if (!visited.Add(cur))
                throw PropertyValidationException.CycleDetected(catalogId, parentId);

            var row = await reader.GetByIdAsync(GetHead(), cur, ct);
            if (row is null)
                return; // parent chain breaks - other validators will report parent not found

            var next = ReadGuid(row.Fields, "parent_property_id");
            if (next is null)
                return;

            cur = next.Value;
        }

        // Too deep => treat as a cycle/invalid chain.
        throw PropertyValidationException.CycleDetected(catalogId, parentId);
    }

    private CatalogHeadDescriptor GetHead()
    {
        if (_head is not null)
            return _head;

        var meta = catalogTypes.GetRequired(TypeCode);
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

        return null;
    }

    private static void RequireNonEmpty(IReadOnlyDictionary<string, object?> fields, string field)
    {
        var v = ReadString(fields, field);
        if (string.IsNullOrWhiteSpace(v))
            throw PropertyValidationException.BuildingAddressRequired(field);
    }

    private static void EnsureNullOrEmpty(IReadOnlyDictionary<string, object?> fields, string field)
    {
        if (!fields.TryGetValue(field, out var raw) || raw is null)
            return;

        var v = ReadString(fields, field);
        if (!string.IsNullOrWhiteSpace(v))
            throw PropertyValidationException.UnitAddressNotAllowed(field);
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

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> fields, string field)
    {
        if (!fields.TryGetValue(field, out var raw) || raw is null)
            return null;

        if (raw is Guid g)
            return g;

        if (raw is string s && Guid.TryParse(s, out var gs))
            return gs;

        if (raw is JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var ge))
                return ge;
        }

        return null;
    }
}
