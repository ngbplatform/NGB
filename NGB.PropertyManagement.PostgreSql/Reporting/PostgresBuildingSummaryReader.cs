using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresBuildingSummaryReader(IUnitOfWork uow) : IBuildingSummaryReader
{
    private const string PropertyCode = "pm.property";

    public async Task<BuildingSummary> GetSummaryAsync(Guid buildingId, DateOnly asOfUtc, CancellationToken ct = default)
    {
        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a building.");

        await uow.EnsureConnectionOpenAsync(ct);

        // 1) Validate building head.
        const string buildingSql = """
SELECT
    p.kind AS Kind,
    p.display AS Display,
    c.is_deleted AS IsDeleted
FROM catalogs c
JOIN cat_pm_property p ON p.catalog_id = c.id
WHERE c.catalog_code = @code
  AND c.id = @building_id;
""";

        var b = await uow.Connection.QuerySingleOrDefaultAsync<BuildingRow>(new CommandDefinition(
            buildingSql,
            new { code = PropertyCode, building_id = buildingId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (b is null)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building was not found.");

        if (b.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building is deleted.");

        if (!string.Equals(b.Kind, "Building", StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected property must be a building.");

        // 2) Counts.
        const string sql = """
WITH units AS (
    SELECT u.catalog_id AS unit_id
    FROM catalogs c
    JOIN cat_pm_property u ON u.catalog_id = c.id
    WHERE c.catalog_code = @code
      AND c.is_deleted = FALSE
      AND u.kind = 'Unit'
      AND u.parent_property_id = @building_id
),
occupied AS (
    SELECT DISTINCT l.property_id AS unit_id
    FROM documents d
    JOIN doc_pm_lease l ON l.document_id = d.id
    JOIN units u ON u.unit_id = l.property_id
    WHERE d.status = @posted
      AND l.start_on_utc <= @as_of
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

        var result = new BuildingSummary(
            BuildingId: buildingId,
            BuildingDisplay: b.Display ?? string.Empty,
            AsOfUtc: asOfUtc,
            TotalUnits: counts.TotalUnits,
            OccupiedUnits: counts.OccupiedUnits);

        result.EnsureInvariant();
        return result;
    }

    private sealed record BuildingRow(string Kind, string? Display, bool IsDeleted);
    
    private sealed record CountsRow(int TotalUnits, int OccupiedUnits);
}
