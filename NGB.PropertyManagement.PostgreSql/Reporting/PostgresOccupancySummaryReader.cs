using Dapper;
using NGB.Core.Documents;
using NGB.Contracts.Common;
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

        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a building.");

        await uow.EnsureConnectionOpenAsync(ct);

        var page = await ReadPageAndTotalsAsync(
            buildingId,
            asOfUtc,
            PagingLimits.BoundOffset(offset),
            limit,
            null,
            false,
            ct);

        var result = new OccupancySummaryPage(page.Rows, page.Total, page.Totals, page.HasMore);
        result.EnsureInvariant();
        return result;
    }

    public async Task<OccupancySummaryPage> GetCursorPageAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        OccupancySummaryPageCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a building.");

        var offset = cursor?.Offset ?? 0;
        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or positive.");

        await uow.EnsureConnectionOpenAsync(ct);
        var page = await ReadPageAndTotalsAsync(
            buildingId,
            asOfUtc,
            PagingLimits.BoundOffset(offset),
            limit,
            cursor,
            true,
            ct);

        var result = new OccupancySummaryPage(page.Rows, page.Total, page.Totals, page.HasMore);
        result.EnsureInvariant();
        return result;
    }

    private async Task<PageAndTotals> ReadPageAndTotalsAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int offset,
        int limit,
        OccupancySummaryPageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        var statsSql = cursor is null
            ? """
stats AS (
    SELECT
        COUNT(*)::int AS BuildingCount,
        COALESCE(SUM(totalunits), 0)::int AS TotalUnits,
        COALESCE(SUM(occupiedunits), 0)::int AS OccupiedUnits
    FROM building_rows
)
"""
            : """
stats AS (
    SELECT
        @known_total::int AS BuildingCount,
        @known_total_units::int AS TotalUnits,
        @known_occupied_units::int AS OccupiedUnits
)
""";
        var sql = $"""
WITH filter_validation AS (
    SELECT
        @building_id::uuid IS NULL OR EXISTS (
            SELECT 1
            FROM catalogs c
            JOIN cat_pm_property p ON p.catalog_id = c.id
            WHERE c.catalog_code = @code
              AND c.id = @building_id::uuid
        ) AS building_found,
        COALESCE((
            SELECT c.is_deleted
            FROM catalogs c
            WHERE c.catalog_code = @code
              AND c.id = @building_id::uuid
        ), FALSE) AS building_deleted,
        (
            SELECT p.kind
            FROM catalogs c
            JOIN cat_pm_property p ON p.catalog_id = c.id
            WHERE c.catalog_code = @code
              AND c.id = @building_id::uuid
        ) AS building_kind
),
candidate_buildings AS (
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
),
building_rows AS (
SELECT
    cb.building_id AS building_id,
    cb.building_display AS building_display,
    COUNT(u.unit_id)::int AS TotalUnits,
    COUNT(o.unit_id)::int AS OccupiedUnits
FROM candidate_buildings cb
LEFT JOIN units u
  ON u.building_id = cb.building_id
LEFT JOIN occupied o
  ON o.building_id = u.building_id
 AND o.unit_id = u.unit_id
GROUP BY cb.building_id, cb.building_display
),
{statsSql},
paged AS (
    SELECT *
    FROM building_rows
    ORDER BY building_display, building_id
    OFFSET @offset
    LIMIT @limit
)
SELECT
    paged.building_id AS BuildingId,
    paged.building_display AS BuildingDisplay,
    paged.totalunits AS TotalUnits,
    paged.occupiedunits AS OccupiedUnits,
    (paged.building_id IS NOT NULL) AS HasRow,
    stats.BuildingCount,
    stats.TotalUnits AS AllTotalUnits,
    stats.OccupiedUnits AS AllOccupiedUnits,
    filter_validation.building_found AS BuildingFound,
    filter_validation.building_deleted AS BuildingDeleted,
    filter_validation.building_kind AS BuildingKind
FROM stats
CROSS JOIN filter_validation
LEFT JOIN paged ON TRUE
ORDER BY paged.building_display, paged.building_id;
""";

        var dbRows = (await uow.Connection.QueryAsync<CombinedRow>(new CommandDefinition(
            sql,
            new
            {
                code = PropertyCode,
                building_id = buildingId,
                as_of = asOfUtc,
                posted = (int)DocumentStatus.Posted,
                offset,
                limit = cursorPaging && limit < int.MaxValue ? limit + 1 : limit,
                known_total = cursor?.Total,
                known_total_units = cursor?.Totals.TotalUnits,
                known_occupied_units = cursor?.Totals.OccupiedUnits
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var stats = dbRows[0];
        ValidateBuildingFilter(buildingId, stats);

        var dataRows = dbRows.Where(row => row.HasRow).ToArray();
        var hasMore = cursorPaging && dataRows.Length > limit;
        var rows = dataRows.Take(limit).Select(row =>
        {
            var result = new OccupancySummaryRow(
                BuildingId: row.BuildingId!.Value,
                BuildingDisplay: row.BuildingDisplay!,
                AsOfUtc: asOfUtc,
                TotalUnits: row.TotalUnits!.Value,
                OccupiedUnits: row.OccupiedUnits!.Value);
            result.EnsureInvariant();
            return result;
        }).ToArray();

        var totals = new OccupancySummaryTotals(
            AsOfUtc: asOfUtc,
            BuildingCount: stats.BuildingCount,
            TotalUnits: stats.AllTotalUnits,
            OccupiedUnits: stats.AllOccupiedUnits);
        totals.EnsureInvariant();

        return new PageAndTotals(rows, stats.BuildingCount, totals, hasMore);
    }

    private static void ValidateBuildingFilter(Guid? buildingId, CombinedRow validation)
    {
        if (buildingId is null)
            return;

        if (!validation.BuildingFound)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building was not found.");

        if (validation.BuildingDeleted)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building is deleted.");

        if (!string.Equals(validation.BuildingKind, "Building", StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected property must be a building.");
    }

    private sealed record CombinedRow(
        Guid? BuildingId,
        string? BuildingDisplay,
        int? TotalUnits,
        int? OccupiedUnits,
        bool HasRow,
        int BuildingCount,
        int AllTotalUnits,
        int AllOccupiedUnits,
        bool BuildingFound,
        bool BuildingDeleted,
        string? BuildingKind);

    private sealed record PageAndTotals(
        IReadOnlyList<OccupancySummaryRow> Rows,
        int Total,
        OccupancySummaryTotals Totals,
        bool HasMore);
}
