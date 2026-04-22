using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresOccupancySummaryReader(IUnitOfWork uow) : IOccupancySummaryReader
{
    private const string PropertyCode = "pm.property";

    public async Task<OccupancySummaryPage> GetPageAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        var validatedBuilding = await ValidateBuildingFilterAsync(buildingId, ct);
        var total = validatedBuilding is null
            ? await ReadTotalAsync(ct)
            : 1;

        var rows = offset >= total
            ? []
            : await ReadPageRowsAsync(validatedBuilding?.BuildingId, asOfUtc, offset, limit, ct);

        var totals = await ReadTotalsAsync(validatedBuilding?.BuildingId, asOfUtc, total, ct);

        var result = new OccupancySummaryPage(rows, total, totals);
        result.EnsureInvariant();
        return result;
    }

    private async Task<ValidatedBuildingRow?> ValidateBuildingFilterAsync(Guid? buildingId, CancellationToken ct)
    {
        if (buildingId is null)
            return null;

        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a building.");

        const string sql = """
SELECT
    c.id AS BuildingId,
    p.kind AS Kind,
    p.display AS BuildingDisplay,
    c.is_deleted AS IsDeleted
FROM catalogs c
JOIN cat_pm_property p ON p.catalog_id = c.id
WHERE c.catalog_code = @code
  AND c.id = @building_id;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ValidatedBuildingRow>(new CommandDefinition(
            sql,
            new { code = PropertyCode, building_id = buildingId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (row is null)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building was not found.");

        if (row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building is deleted.");

        if (!string.Equals(row.Kind, "Building", StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected property must be a building.");

        return row;
    }

    private async Task<int> ReadTotalAsync(CancellationToken ct)
    {
        const string sql = """
SELECT COUNT(*)::int
FROM catalogs c
JOIN cat_pm_property p ON p.catalog_id = c.id
WHERE c.catalog_code = @code
  AND c.is_deleted = FALSE
  AND p.kind = 'Building';
""";

        return await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            sql,
            new { code = PropertyCode },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private async Task<IReadOnlyList<OccupancySummaryRow>> ReadPageRowsAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int offset,
        int limit,
        CancellationToken ct)
    {
        const string sql = """
WITH candidate_buildings AS (
    SELECT
        c.id AS building_id,
        COALESCE(NULLIF(btrim(p.display), ''), '[Building]') AS building_display
    FROM catalogs c
    JOIN cat_pm_property p ON p.catalog_id = c.id
    WHERE c.catalog_code = @code
      AND c.is_deleted = FALSE
      AND p.kind = 'Building'
      AND (@building_id::uuid IS NULL OR c.id = @building_id::uuid)
),
paged_buildings AS (
    SELECT building_id, building_display
    FROM candidate_buildings
    ORDER BY building_display, building_id
    OFFSET @offset
    LIMIT @limit
),
units AS (
    SELECT
        pb.building_id,
        u.catalog_id AS unit_id
    FROM paged_buildings pb
    JOIN cat_pm_property u
      ON u.parent_property_id = pb.building_id
     AND u.kind = 'Unit'
    JOIN catalogs c
      ON c.id = u.catalog_id
     AND c.catalog_code = @code
     AND c.is_deleted = FALSE
),
occupied AS (
    SELECT DISTINCT
        u.building_id,
        l.property_id AS unit_id
    FROM units u
    JOIN doc_pm_lease l
      ON l.property_id = u.unit_id
    JOIN documents d
      ON d.id = l.document_id
     AND d.status = @posted
    WHERE l.start_on_utc <= @as_of
      AND @as_of <= COALESCE(l.end_on_utc, 'infinity'::date)
)
SELECT
    pb.building_id AS BuildingId,
    pb.building_display AS BuildingDisplay,
    COUNT(u.unit_id)::int AS TotalUnits,
    COUNT(o.unit_id)::int AS OccupiedUnits
FROM paged_buildings pb
LEFT JOIN units u
  ON u.building_id = pb.building_id
LEFT JOIN occupied o
  ON o.building_id = u.building_id
 AND o.unit_id = u.unit_id
GROUP BY pb.building_id, pb.building_display
ORDER BY pb.building_display, pb.building_id;
""";

        var rows = await uow.Connection.QueryAsync<PageRow>(new CommandDefinition(
            sql,
            new
            {
                code = PropertyCode,
                building_id = buildingId,
                as_of = asOfUtc,
                posted = (int)DocumentStatus.Posted,
                offset,
                limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.Select(row =>
        {
            var result = new OccupancySummaryRow(
                BuildingId: row.BuildingId,
                BuildingDisplay: row.BuildingDisplay,
                AsOfUtc: asOfUtc,
                TotalUnits: row.TotalUnits,
                OccupiedUnits: row.OccupiedUnits);
            result.EnsureInvariant();
            return result;
        }).ToArray();
    }

    private async Task<OccupancySummaryTotals> ReadTotalsAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int buildingCount,
        CancellationToken ct)
    {
        const string sql = """
WITH candidate_buildings AS (
    SELECT c.id AS building_id
    FROM catalogs c
    JOIN cat_pm_property p ON p.catalog_id = c.id
    WHERE c.catalog_code = @code
      AND c.is_deleted = FALSE
      AND p.kind = 'Building'
      AND (@building_id::uuid IS NULL OR c.id = @building_id::uuid)
),
units AS (
    SELECT
        cb.building_id,
        u.catalog_id AS unit_id
    FROM candidate_buildings cb
    JOIN cat_pm_property u
      ON u.parent_property_id = cb.building_id
     AND u.kind = 'Unit'
    JOIN catalogs c
      ON c.id = u.catalog_id
     AND c.catalog_code = @code
     AND c.is_deleted = FALSE
),
occupied AS (
    SELECT DISTINCT
        u.building_id,
        l.property_id AS unit_id
    FROM units u
    JOIN doc_pm_lease l
      ON l.property_id = u.unit_id
    JOIN documents d
      ON d.id = l.document_id
     AND d.status = @posted
    WHERE l.start_on_utc <= @as_of
      AND @as_of <= COALESCE(l.end_on_utc, 'infinity'::date)
)
SELECT
    (SELECT COUNT(*)::int FROM units) AS TotalUnits,
    (SELECT COUNT(*)::int FROM occupied) AS OccupiedUnits;
""";

        var counts = await uow.Connection.QuerySingleAsync<CountsRow>(new CommandDefinition(
            sql,
            new
            {
                code = PropertyCode,
                building_id = buildingId,
                as_of = asOfUtc,
                posted = (int)DocumentStatus.Posted
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        var totals = new OccupancySummaryTotals(
            AsOfUtc: asOfUtc,
            BuildingCount: buildingCount,
            TotalUnits: counts.TotalUnits,
            OccupiedUnits: counts.OccupiedUnits);
        totals.EnsureInvariant();
        return totals;
    }

    private sealed record ValidatedBuildingRow(Guid BuildingId, string Kind, string? BuildingDisplay, bool IsDeleted);
    private sealed record PageRow(Guid BuildingId, string BuildingDisplay, int TotalUnits, int OccupiedUnits);
    private sealed record CountsRow(int TotalUnits, int OccupiedUnits);
}
