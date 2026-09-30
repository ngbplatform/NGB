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

        // Validate the head and calculate both counts from one consistent database snapshot.
        // The building predicate inside units also prevents unnecessary lease work for invalid heads.
        const string sql = """
WITH building AS (
    SELECT
        p.kind AS kind,
        p.display AS display,
        c.is_deleted AS is_deleted
    FROM catalogs c
    JOIN cat_pm_property p ON p.catalog_id = c.id
    WHERE c.catalog_code = @code
      AND c.id = @building_id
),
units AS (
    SELECT u.catalog_id AS unit_id
    FROM catalogs c
    JOIN cat_pm_property u ON u.catalog_id = c.id
    CROSS JOIN building b
    WHERE c.catalog_code = @code
      AND c.is_deleted = FALSE
      AND u.kind = 'Unit'
      AND u.parent_property_id = @building_id
      AND b.is_deleted = FALSE
      AND LOWER(b.kind) = 'building'
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
    b.kind AS Kind,
    b.display AS Display,
    b.is_deleted AS IsDeleted,
    (SELECT COUNT(*)::int FROM units) AS TotalUnits,
    (SELECT COUNT(*)::int FROM occupied) AS OccupiedUnits
FROM building b;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<SummaryRow>(new CommandDefinition(
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

        if (row is null)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building was not found.");

        if (row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected building is deleted.");

        if (!string.Equals(row.Kind, "Building", StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(nameof(buildingId), "Selected property must be a building.");

        var result = new BuildingSummary(
            BuildingId: buildingId,
            BuildingDisplay: row.Display,
            AsOfUtc: asOfUtc,
            TotalUnits: row.TotalUnits,
            OccupiedUnits: row.OccupiedUnits);

        result.EnsureInvariant();
        return result;
    }

    private sealed record SummaryRow(
        string Kind,
        string Display,
        bool IsDeleted,
        int TotalUnits,
        int OccupiedUnits);
}
