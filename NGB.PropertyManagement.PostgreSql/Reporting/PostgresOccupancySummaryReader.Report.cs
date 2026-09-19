using System.Runtime.CompilerServices;
using Dapper;
using NGB.Core.Documents;
using NGB.PostgreSql.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed partial class PostgresOccupancySummaryReader
{
    private static string ReportRowsSql(bool paged, bool seek) => $"""
WITH candidate_buildings AS (
    SELECT p.catalog_id AS building_id, COALESCE(NULLIF(BTRIM(p.display), ''), '[Building]') AS building_display
    FROM cat_pm_property p
    JOIN catalogs c ON c.id = p.catalog_id AND c.catalog_code = @code AND c.is_deleted = FALSE
    WHERE p.kind = 'Building' AND (@building_id::uuid IS NULL OR p.catalog_id = @building_id)
),
selected_buildings AS MATERIALIZED (
    SELECT * FROM candidate_buildings
    {(seek ? "WHERE (building_display, building_id) > (@after_display::text, @after_id::uuid)" : "")}
    ORDER BY building_display, building_id
    {(paged ? "LIMIT @limit" : "")}
),
active_units AS (
    SELECT u.catalog_id AS unit_id, u.parent_property_id AS building_id
    FROM selected_buildings b
    JOIN cat_pm_property u ON u.parent_property_id = b.building_id AND u.kind = 'Unit'
    JOIN catalogs c ON c.id = u.catalog_id AND c.catalog_code = @code AND c.is_deleted = FALSE
),
occupied_units AS (
    SELECT DISTINCT l.property_id AS unit_id
    FROM doc_pm_lease l
    JOIN documents d ON d.id = l.document_id AND d.type_code = 'pm.lease' AND d.status = @posted
    JOIN active_units u ON u.unit_id = l.property_id
    WHERE l.start_on_utc <= @as_of AND (l.end_on_utc IS NULL OR l.end_on_utc >= @as_of)
),
unit_counts AS (
    SELECT u.building_id, COUNT(*)::int AS total_units, COUNT(o.unit_id)::int AS occupied_units
    FROM active_units u LEFT JOIN occupied_units o ON o.unit_id = u.unit_id
    GROUP BY u.building_id
),
building_rows AS (
    SELECT b.building_id AS "BuildingId", b.building_display AS "BuildingDisplay", @as_of::date AS "AsOfUtc",
        COALESCE(c.total_units, 0)::int AS "TotalUnits", COALESCE(c.occupied_units, 0)::int AS "OccupiedUnits"
    FROM selected_buildings b LEFT JOIN unit_counts c ON c.building_id = b.building_id
)
""";

    private static object ReportArgs(
        Guid? buildingId,
        DateOnly asOf,
        OccupancySummaryContinuation? after = null,
        int limit = 0)
        => new
        { 
            code = PropertyCode,
            building_id = buildingId,
            as_of = asOf,
            posted = (int)DocumentStatus.Posted,
            after_display = after?.BuildingDisplay,
            after_id = after?.BuildingId,
            limit
        };

    private async Task ValidateReportFilterAsync(Guid? buildingId, CancellationToken ct)
    {
        if (buildingId is null)
            return;

        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a building.");

        await uow.EnsureConnectionOpenAsync(ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ReportFilterRow>(new CommandDefinition("""
            SELECT p.kind AS Kind, c.is_deleted AS IsDeleted FROM catalogs c
            JOIN cat_pm_property p ON p.catalog_id = c.id
            WHERE c.id = @buildingId AND c.catalog_code = @code
            """,
            new
            {
                buildingId, code = PropertyCode
            },
            uow.Transaction,
            cancellationToken: ct));

        if (row is null)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building was not found.");

        if (row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building is deleted.");

        if (row.Kind != "Building")
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected property must be a building.");
    }

    public async Task<OccupancySummarySlice> GetSliceAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        OccupancySummaryContinuation? after,
        int limit,
        CancellationToken ct = default)
    {
        if (limit is <= 0 or int.MaxValue)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive and bounded.");

        await uow.EnsureConnectionOpenAsync(ct);
        await ValidateReportFilterAsync(buildingId, ct);

        var rows = (await uow.Connection.QueryAsync<OccupancySummaryRow>(new CommandDefinition(
                ReportRowsSql(true, after is not null) + "SELECT * FROM building_rows ORDER BY \"BuildingDisplay\", \"BuildingId\";",
                ReportArgs(buildingId, asOfUtc, after, limit + 1),
                uow.Transaction,
                cancellationToken: ct)))
            .AsList();

        var hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        foreach (var row in rows)
        {
            row.EnsureInvariant();
        }

        return new(
            rows,
            hasMore,
            hasMore 
                ? new(rows[^1].BuildingDisplay, rows[^1].BuildingId, checked((after?.Offset ?? 0) + rows.Count))
                : null);
    }

    public async Task<OccupancySummaryTotals> GetTotalsAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        await ValidateReportFilterAsync(buildingId, ct);

        var totals = await uow.Connection.QuerySingleAsync<OccupancySummaryTotals>(new CommandDefinition(
            ReportRowsSql(false, false) + """
            SELECT @as_of::date AS AsOfUtc, COUNT(*)::int AS BuildingCount,
                COALESCE(SUM("TotalUnits"),0)::int AS TotalUnits,
                COALESCE(SUM("OccupiedUnits"),0)::int AS OccupiedUnits FROM building_rows
            """, ReportArgs(buildingId, asOfUtc),
            uow.Transaction,
            cancellationToken: ct));
        totals.EnsureInvariant();

        return totals;
    }

    public async IAsyncEnumerable<IReadOnlyList<OccupancySummaryRow>> ReadAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        await ValidateReportFilterAsync(buildingId, ct);

        await foreach (var batch in PostgresReportCursorStream.ReadAsync<OccupancySummaryRow>(uow,
            ReportRowsSql(false, false) + "SELECT * FROM building_rows ORDER BY \"BuildingDisplay\", \"BuildingId\";",
            ReportArgs(buildingId, asOfUtc), ct))
        {
            foreach (var row in batch)
            {
                row.EnsureInvariant();
            }

            yield return batch;
        }
    }

    private sealed record ReportFilterRow(string Kind, bool IsDeleted);
}
