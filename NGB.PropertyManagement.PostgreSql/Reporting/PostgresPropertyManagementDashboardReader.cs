using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresPropertyManagementDashboardReader(IUnitOfWork uow)
    : IPropertyManagementDashboardReader
{
    public async Task<PropertyManagementDashboardSnapshot> GetAsync(DateOnly asOfUtc, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH active_properties AS (
    SELECT property.catalog_id, property.kind, property.parent_property_id
    FROM cat_pm_property property
    JOIN catalogs catalog
      ON catalog.id = property.catalog_id
     AND catalog.catalog_code = @property_code
     AND catalog.is_deleted = FALSE
),
active_units AS (
    SELECT catalog_id AS unit_id
    FROM active_properties
    WHERE kind = 'Unit'
),
occupied_now AS (
    SELECT DISTINCT lease.property_id AS unit_id
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
    JOIN active_units unit ON unit.unit_id = lease.property_id
    WHERE lease.start_on_utc <= @as_of
      AND @as_of <= COALESCE(lease.end_on_utc, 'infinity'::date)
),
occupied_future AS (
    SELECT DISTINCT lease.property_id AS unit_id
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
    JOIN active_units unit ON unit.unit_id = lease.property_id
    WHERE lease.start_on_utc <= @future_as_of
      AND @future_as_of <= COALESCE(lease.end_on_utc, 'infinity'::date)
)
SELECT
    (SELECT COUNT(*)::integer FROM active_properties WHERE kind = 'Building') AS BuildingCount,
    (SELECT COUNT(*)::integer FROM active_units) AS TotalUnits,
    (SELECT COUNT(*)::integer FROM occupied_now) AS OccupiedUnits,
    (SELECT COUNT(*)::integer FROM occupied_future) AS FutureOccupiedUnits;

WITH months AS (
    SELECT
        month_start::date AS month,
        LEAST((month_start + INTERVAL '1 month - 1 day')::date, @as_of::date) AS point_date
    FROM generate_series(
        date_trunc('month', @as_of::date) - INTERVAL '11 months',
        date_trunc('month', @as_of::date),
        INTERVAL '1 month') month_start
),
active_units AS (
    SELECT property.catalog_id AS unit_id
    FROM cat_pm_property property
    JOIN catalogs catalog
      ON catalog.id = property.catalog_id
     AND catalog.catalog_code = @property_code
     AND catalog.is_deleted = FALSE
    WHERE property.kind = 'Unit'
),
posted_leases AS (
    SELECT lease.*
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
)
SELECT
    month.month AS Month,
    COUNT(DISTINCT lease.property_id) FILTER (WHERE lease.document_id IS NOT NULL)::integer AS OccupiedUnits,
    (COUNT(DISTINCT unit.unit_id)
        - COUNT(DISTINCT lease.property_id) FILTER (WHERE lease.document_id IS NOT NULL))::integer AS VacantUnits
FROM months month
CROSS JOIN active_units unit
LEFT JOIN posted_leases lease
  ON lease.property_id = unit.unit_id
 AND lease.start_on_utc <= month.point_date
 AND month.point_date <= COALESCE(lease.end_on_utc, 'infinity'::date)
GROUP BY month.month
ORDER BY month.month;

WITH posted_leases AS (
    SELECT lease.*
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
)
SELECT
    COUNT(*) FILTER (
        WHERE end_on_utc BETWEEN @as_of::date AND @future_as_of::date)::integer AS Expiring30Count,
    COUNT(*) FILTER (
        WHERE start_on_utc BETWEEN @as_of::date AND @event_end::date)::integer AS UpcomingMoveInCount,
    COUNT(*) FILTER (
        WHERE end_on_utc BETWEEN @as_of::date AND @event_end::date)::integer AS UpcomingMoveOutCount
FROM posted_leases;

WITH events AS (
    SELECT
        'Move-in'::text AS Kind,
        lease.start_on_utc AS Date,
        lease.document_id AS LeaseId,
        lease.display AS LeaseDisplay,
        COALESCE(NULLIF(BTRIM(property.display), ''), lease.property_id::text) AS PropertyDisplay
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
    LEFT JOIN cat_pm_property property ON property.catalog_id = lease.property_id
    WHERE lease.start_on_utc BETWEEN @as_of::date AND @event_end::date

    UNION ALL

    SELECT
        'Move-out'::text,
        lease.end_on_utc,
        lease.document_id,
        lease.display,
        COALESCE(NULLIF(BTRIM(property.display), ''), lease.property_id::text)
    FROM doc_pm_lease lease
    JOIN documents document
      ON document.id = lease.document_id
     AND document.status = @posted
    LEFT JOIN cat_pm_property property ON property.catalog_id = lease.property_id
    WHERE lease.end_on_utc BETWEEN @as_of::date AND @event_end::date
)
SELECT Kind, Date, LeaseId, LeaseDisplay, PropertyDisplay
FROM events
ORDER BY Date, Kind, LeaseId
LIMIT @event_limit;

WITH months AS (
    SELECT month_start::date AS month
    FROM generate_series(
        date_trunc('month', @as_of::date) - INTERVAL '11 months',
        date_trunc('month', @as_of::date),
        INTERVAL '1 month') month_start
),
activity AS (
    SELECT date_trunc('month', charge.due_on_utc)::date AS month, charge.amount AS billed, 0::numeric AS collected
    FROM doc_pm_rent_charge charge
    JOIN documents document ON document.id = charge.document_id AND document.status = @posted
    WHERE charge.due_on_utc BETWEEN (SELECT MIN(month) FROM months) AND @as_of::date

    UNION ALL
    SELECT date_trunc('month', charge.due_on_utc)::date, charge.amount, 0::numeric
    FROM doc_pm_receivable_charge charge
    JOIN documents document ON document.id = charge.document_id AND document.status = @posted
    WHERE charge.due_on_utc BETWEEN (SELECT MIN(month) FROM months) AND @as_of::date

    UNION ALL
    SELECT date_trunc('month', charge.due_on_utc)::date, charge.amount, 0::numeric
    FROM doc_pm_late_fee_charge charge
    JOIN documents document ON document.id = charge.document_id AND document.status = @posted
    WHERE charge.due_on_utc BETWEEN (SELECT MIN(month) FROM months) AND @as_of::date

    UNION ALL
    SELECT date_trunc('month', payment.received_on_utc)::date, 0::numeric, payment.amount
    FROM doc_pm_receivable_payment payment
    JOIN documents document ON document.id = payment.document_id AND document.status = @posted
    WHERE payment.received_on_utc BETWEEN (SELECT MIN(month) FROM months) AND @as_of::date

    UNION ALL
    SELECT date_trunc('month', returned.returned_on_utc)::date, 0::numeric, -returned.amount
    FROM doc_pm_receivable_returned_payment returned
    JOIN documents document ON document.id = returned.document_id AND document.status = @posted
    WHERE returned.returned_on_utc BETWEEN (SELECT MIN(month) FROM months) AND @as_of::date
)
SELECT
    month.month AS Month,
    COALESCE(SUM(activity.billed), 0) AS Billed,
    COALESCE(SUM(activity.collected), 0) AS Collected
FROM months month
LEFT JOIN activity ON activity.month = month.month
GROUP BY month.month
ORDER BY month.month;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                property_code = PropertyManagementCodes.Property,
                posted = (int)DocumentStatus.Posted,
                as_of = asOfUtc,
                future_as_of = asOfUtc.AddDays(30),
                event_end = asOfUtc.AddDays(14),
                event_limit = 6
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await using var grid = await uow.Connection.QueryMultipleAsync(command);
        var portfolio = await grid.ReadSingleAsync<PortfolioRow>();
        var occupancy = (await grid.ReadAsync<PropertyManagementDashboardOccupancySnapshot>()).AsList();
        var leaseStats = await grid.ReadSingleAsync<LeaseStatsRow>();
        var leaseEvents = (await grid.ReadAsync<PropertyManagementDashboardLeaseEventSnapshot>()).AsList();
        var collections = (await grid.ReadAsync<PropertyManagementDashboardCollectionsSnapshot>()).AsList();

        return new PropertyManagementDashboardSnapshot(
            new PropertyManagementDashboardPortfolioSnapshot(
                portfolio.BuildingCount,
                portfolio.TotalUnits,
                portfolio.OccupiedUnits,
                portfolio.FutureOccupiedUnits),
            new PropertyManagementDashboardLeaseSnapshot(
                leaseStats.Expiring30Count,
                leaseStats.UpcomingMoveInCount,
                leaseStats.UpcomingMoveOutCount,
                leaseEvents),
            occupancy,
            collections);
    }

    private sealed record PortfolioRow(
        int BuildingCount,
        int TotalUnits,
        int OccupiedUnits,
        int FutureOccupiedUnits);

    private sealed record LeaseStatsRow(int Expiring30Count, int UpcomingMoveInCount, int UpcomingMoveOutCount);
}
