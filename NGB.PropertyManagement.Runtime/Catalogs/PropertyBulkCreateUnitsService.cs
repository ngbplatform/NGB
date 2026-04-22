using System.Globalization;
using System.Text.Json;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Catalogs;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs.Validation;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Catalogs;

/// <summary>
/// Bulk creation of Unit properties under a Building for pm.property.
///
/// Semantics:
/// - Only creates missing units; existing active unit_no values are treated as duplicates and returned in the summary.
/// - Uses a single DB transaction and a catalog advisory lock on the building.
/// </summary>
public sealed class PropertyBulkCreateUnitsService(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    ICatalogTypeRegistry catalogTypes,
    ICatalogRepository catalogs,
    ICatalogReader reader,
    ICatalogWriter writer,
    ICatalogValidatorResolver validators,
    TimeProvider timeProvider,
    IAuditLogService? audit = null)
    : IPropertyBulkCreateUnitsService
{
    private const int MaxUnitsPerRequest = 5000;
    private const int PreviewSampleLimit = 50;

    public async Task<PropertyBulkCreateUnitsResponse> BulkCreateUnitsAsync(
        PropertyBulkCreateUnitsRequest request,
        CancellationToken ct)
        => await ExecuteAsync(request, dryRun: false, ct);

    public async Task<PropertyBulkCreateUnitsResponse> DryRunAsync(
        PropertyBulkCreateUnitsRequest request,
        CancellationToken ct)
        => await ExecuteAsync(request, dryRun: true, ct);

    private async Task<PropertyBulkCreateUnitsResponse> ExecuteAsync(
        PropertyBulkCreateUnitsRequest request,
        bool dryRun,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.BuildingId == Guid.Empty)
            throw PropertyBulkCreateUnitsValidationException.BuildingRequired();

        if (request.Step <= 0)
            throw PropertyBulkCreateUnitsValidationException.StepMustBePositive(request.Step);

        if (request.FromInclusive <= 0 || request.ToInclusive <= 0)
            throw PropertyBulkCreateUnitsValidationException.RangeMustBePositive(request.FromInclusive, request.ToInclusive);

        if (request.FromInclusive > request.ToInclusive)
            throw PropertyBulkCreateUnitsValidationException.RangeInvalid(request.FromInclusive, request.ToInclusive);

        if (string.IsNullOrWhiteSpace(request.UnitNoFormat))
            throw PropertyBulkCreateUnitsValidationException.UnitNoFormatRequired();

        if (!request.UnitNoFormat.Contains("{0", StringComparison.Ordinal))
            throw PropertyBulkCreateUnitsValidationException.UnitNoFormatMustIncludeNumberPlaceholder(request.UnitNoFormat);

        if (request.FloorSize is not null && request.FloorSize <= 0)
            throw PropertyBulkCreateUnitsValidationException.FloorSizeMustBePositive(request.FloorSize.Value);

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction: true,
            async innerCt =>
            {
                // Serialize concurrent bulk operations for the same building.
                await locks.LockCatalogAsync(request.BuildingId, innerCt);

                var head = BuildHeadDescriptor();

                // Validate building id once using the configured validators (friendly errors).
                // We validate a single representative Unit upsert - this checks:
                // - building exists, not deleted
                // - building kind is Building
                // - no cycles
                // Duplicate check is done with a probe unit_no and is cheap.
                var probeFields = new Dictionary<string, object?>
                {
                    ["kind"] = "Unit",
                    ["parent_property_id"] = request.BuildingId,
                    ["unit_no"] = "__probe__"
                };

                var probeContext = new CatalogUpsertValidationContext(
                    TypeCode: PropertyManagementCodes.Property,
                    CatalogId: Guid.CreateVersion7(),
                    IsCreate: true,
                    Fields: probeFields);

                foreach (var v in validators.ResolveUpsertValidators(PropertyManagementCodes.Property))
                {
                    await v.ValidateUpsertAsync(probeContext, innerCt);
                }

                // Preload existing active unit_nos under the building.
                var existing = await LoadExistingUnitNosAsync(head, request.BuildingId, innerCt);

                // Generate requested unit_nos.
                var requestedNos = GenerateUnitNos(request);

                if (requestedNos.Count > MaxUnitsPerRequest)
                    throw PropertyBulkCreateUnitsValidationException.TooManyUnitsRequested(requestedNos.Count, MaxUnitsPerRequest);

                var preview = requestedNos.Take(PreviewSampleLimit).ToList();
                var duplicateNos = requestedNos
                    .Where(existing.Contains)
                    .ToList();

                var unitsToCreate = requestedNos
                    .Where(unitNo => !existing.Contains(unitNo))
                    .ToList();

                IReadOnlyList<string> createdNos = dryRun ? [] : unitsToCreate;
                var createdIds = new List<Guid>(unitsToCreate.Count);
                var wouldCreateCount = unitsToCreate.Count;

                if (!dryRun && unitsToCreate.Count > 0)
                {
                    var nowUtc = timeProvider.GetUtcNowDateTime();
                    var pendingUnits = unitsToCreate
                        .Select(unitNo => new PendingUnit(Guid.CreateVersion7(), unitNo))
                        .ToArray();

                    await catalogs.CreateManyAsync(
                        pendingUnits
                            .Select(unit => new CatalogRecord
                            {
                                Id = unit.Id,
                                CatalogCode = PropertyManagementCodes.Property,
                                IsDeleted = false,
                                CreatedAtUtc = nowUtc,
                                UpdatedAtUtc = nowUtc
                            })
                            .ToArray(),
                        innerCt);

                    await writer.UpsertHeadsAsync(
                        head,
                        pendingUnits
                            .Select(unit => new CatalogHeadWriteRow(
                                unit.Id,
                                [
                                    new CatalogHeadValue("kind", ColumnType.String, "Unit"),
                                    new CatalogHeadValue("parent_property_id", ColumnType.Guid, request.BuildingId),
                                    new CatalogHeadValue("unit_no", ColumnType.String, unit.UnitNo)
                                ]))
                            .ToArray(),
                        innerCt);

                    if (audit is not null)
                    {
                        await audit.WriteBatchAsync(
                            pendingUnits
                                .Select(unit => new AuditLogWriteRequest(
                                    EntityKind: AuditEntityKind.Catalog,
                                    EntityId: unit.Id,
                                    ActionCode: AuditActionCodes.CatalogCreate,
                                    Changes:
                                    [
                                        CreateAuditChange("kind", null, "Unit"),
                                        CreateAuditChange("parent_property_id", null, request.BuildingId),
                                        CreateAuditChange("unit_no", null, unit.UnitNo)
                                    ],
                                    Metadata: new { catalogCode = PropertyManagementCodes.Property }))
                                .ToArray(),
                            innerCt);
                    }

                    createdIds.AddRange(pendingUnits.Select(unit => unit.Id));
                }

                // Limit samples to keep response size bounded.
                const int sampleLimit = 100;

                return new PropertyBulkCreateUnitsResponse(
                    BuildingId: request.BuildingId,
                    RequestedCount: requestedNos.Count,
                    CreatedCount: createdIds.Count,
                    DuplicateCount: duplicateNos.Count,
                    CreatedIds: createdIds,
                    CreatedUnitNosSample: createdNos.Take(sampleLimit).ToList(),
                    DuplicateUnitNosSample: duplicateNos.Take(sampleLimit).ToList())
                {
                    WouldCreateCount = dryRun ? wouldCreateCount : createdIds.Count,
                    PreviewUnitNosSample = preview,
                    IsDryRun = dryRun
                };
            },
            ct);
    }

    private CatalogHeadDescriptor BuildHeadDescriptor()
    {
        var meta = catalogTypes.GetRequired(PropertyManagementCodes.Property);

        var headTable = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has no Head table metadata.");

        var displayColumn = meta.Presentation.DisplayColumn;
        if (string.IsNullOrWhiteSpace(displayColumn))
            throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has empty Presentation.DisplayColumn.");

        var scalarColumns = headTable.Columns
            .Where(x => !string.Equals(x.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new CatalogHeadDescriptor(
            CatalogCode: meta.CatalogCode,
            HeadTableName: headTable.TableName,
            DisplayColumn: displayColumn,
            Columns: scalarColumns
                .Select(c => new CatalogHeadColumn(c.ColumnName, c.ColumnType))
                .ToList());
    }

    private static List<string> GenerateUnitNos(PropertyBulkCreateUnitsRequest request)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var n = request.FromInclusive; n <= request.ToInclusive; n += request.Step)
        {
            var floor = request.FloorSize is null
                ? 0
                : (n - request.FromInclusive) / request.FloorSize.Value + 1;

            string unitNo;
            try
            {
                unitNo = string.Format(CultureInfo.InvariantCulture, request.UnitNoFormat, n, floor);
            }
            catch (FormatException ex)
            {
                throw PropertyBulkCreateUnitsValidationException.UnitNoFormatInvalid(request.UnitNoFormat, ex);
            }

            unitNo = unitNo.Trim();
            if (unitNo.Length == 0)
                throw PropertyBulkCreateUnitsValidationException.GeneratedUnitNoEmpty(request.UnitNoFormat);

            if (seen.Add(unitNo))
                list.Add(unitNo);
        }

        return list;
    }

    private static async Task<HashSet<string>> LoadExistingUnitNosAsync(
        CatalogHeadDescriptor head,
        Guid buildingId,
        ICatalogReader reader,
        CancellationToken ct)
    {
        var q = new CatalogQuery(
            Search: null,
            Filters: new List<CatalogFilter>
            {
                new("kind", "Unit"),
                new("parent_property_id", buildingId.ToString())
            })
        {
            SoftDeleteFilterMode = SoftDeleteFilterMode.Active
        };

        var result = new HashSet<string>(StringComparer.Ordinal);
        const int pageSize = 2000;

        for (var offset = 0; ; offset += pageSize)
        {
            var rows = await reader.GetPageAsync(head, q, offset, pageSize, ct);
            if (rows.Count == 0)
                break;

            foreach (var r in rows)
            {
                if (!r.Fields.TryGetValue("unit_no", out var raw) || raw is null)
                    continue;

                var s = raw.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    result.Add(s.Trim());
            }

            if (rows.Count < pageSize)
                break;
        }

        return result;
    }

    private Task<HashSet<string>> LoadExistingUnitNosAsync(CatalogHeadDescriptor head, Guid buildingId, CancellationToken ct)
        => LoadExistingUnitNosAsync(head, buildingId, reader, ct);

    private static AuditFieldChange CreateAuditChange(string fieldPath, object? oldValue, object? newValue)
        => new(
            FieldPath: fieldPath,
            OldValueJson: oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson: newValue is null ? null : JsonSerializer.Serialize(newValue));

    private sealed record PendingUnit(Guid Id, string UnitNo);
}
