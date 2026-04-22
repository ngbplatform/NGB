using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresMaintenanceQueueReader(IUnitOfWork uow) : IMaintenanceQueueReader
{
    private const string PropertyCode = PropertyManagementCodes.Property;
    private const string PartyCode = PropertyManagementCodes.Party;
    private const string MaintenanceCategoryCode = PropertyManagementCodes.MaintenanceCategory;

    private static readonly string QueueCte = """
WITH candidate_requests AS (
    SELECT
        mr.document_id AS request_id,
        mr.display AS request_display,
        mr.subject AS subject,
        mr.requested_at_utc AS requested_at_utc,
        mr.property_id AS property_id,
        COALESCE(NULLIF(BTRIM(req_prop.display), ''), '[Property]') AS property_display,
        CASE
            WHEN req_prop.kind = 'Building' THEN req_prop.catalog_id
            ELSE req_prop.parent_property_id
        END AS building_id,
        COALESCE(
            CASE
                WHEN req_prop.kind = 'Building' THEN NULLIF(BTRIM(req_prop.display), '')
                ELSE NULLIF(BTRIM(build_prop.display), '')
            END,
            '[Building]') AS building_display,
        mr.party_id AS requested_by_party_id,
        COALESCE(NULLIF(BTRIM(req_party.display), ''), '[Party]') AS requested_by_display,
        mr.category_id AS category_id,
        COALESCE(NULLIF(BTRIM(cat.display), ''), '[Category]') AS category_display,
        mr.priority AS priority
    FROM doc_pm_maintenance_request mr
    JOIN documents req_doc
      ON req_doc.id = mr.document_id
     AND req_doc.status = @posted
    JOIN cat_pm_property req_prop
      ON req_prop.catalog_id = mr.property_id
    LEFT JOIN cat_pm_property build_prop
      ON build_prop.catalog_id = CASE
          WHEN req_prop.kind = 'Building' THEN req_prop.catalog_id
          ELSE req_prop.parent_property_id
      END
    LEFT JOIN cat_pm_party req_party
      ON req_party.catalog_id = mr.party_id
    LEFT JOIN cat_pm_maintenance_category cat
      ON cat.catalog_id = mr.category_id
    WHERE mr.requested_at_utc <= @as_of
      AND (@property_id::uuid IS NULL OR mr.property_id = @property_id::uuid)
      AND (@building_id::uuid IS NULL OR CASE
            WHEN req_prop.kind = 'Building' THEN req_prop.catalog_id
            ELSE req_prop.parent_property_id
          END = @building_id::uuid)
      AND (@category_id::uuid IS NULL OR mr.category_id = @category_id::uuid)
      AND (@priority::text IS NULL OR mr.priority = @priority::text)
),
posted_work_orders AS (
    SELECT
        wo.document_id AS work_order_id,
        wo.display AS work_order_display,
        wo.request_id AS request_id,
        wo.assigned_party_id AS assigned_party_id,
        COALESCE(NULLIF(BTRIM(assigned_party.display), ''), '[Party]') AS assigned_party_display,
        wo.due_by_utc AS due_by_utc
    FROM doc_pm_work_order wo
    JOIN documents wo_doc
      ON wo_doc.id = wo.document_id
     AND wo_doc.status = @posted
    LEFT JOIN cat_pm_party assigned_party
      ON assigned_party.catalog_id = wo.assigned_party_id
),
open_work_orders AS (
    SELECT
        pwo.work_order_id,
        pwo.work_order_display,
        pwo.request_id,
        pwo.assigned_party_id,
        pwo.assigned_party_display,
        pwo.due_by_utc
    FROM posted_work_orders pwo
    WHERE (@assigned_party_id::uuid IS NULL OR pwo.assigned_party_id = @assigned_party_id::uuid)
      AND NOT EXISTS (
          SELECT 1
          FROM doc_pm_work_order_completion wc
          JOIN documents wc_doc
            ON wc_doc.id = wc.document_id
           AND wc_doc.status = @posted
          WHERE wc.work_order_id = pwo.work_order_id
            AND wc.closed_at_utc <= @as_of)
),
queue_rows AS (
    SELECT
        cr.request_id AS request_id,
        cr.request_display AS request_display,
        cr.subject AS subject,
        cr.requested_at_utc AS requested_at_utc,
        (@as_of::date - cr.requested_at_utc)::int AS aging_days,
        cr.building_id AS building_id,
        cr.building_display AS building_display,
        cr.property_id AS property_id,
        cr.property_display AS property_display,
        cr.category_id AS category_id,
        cr.category_display AS category_display,
        cr.priority AS priority,
        cr.requested_by_party_id AS requested_by_party_id,
        cr.requested_by_display AS requested_by_display,
        NULL::uuid AS work_order_id,
        NULL::text AS work_order_display,
        NULL::uuid AS assigned_party_id,
        NULL::text AS assigned_party_display,
        NULL::date AS due_by_utc,
        'Requested'::text AS queue_state
    FROM candidate_requests cr
    WHERE @assigned_party_id::uuid IS NULL
      AND NOT EXISTS (
          SELECT 1
          FROM posted_work_orders pwo
          WHERE pwo.request_id = cr.request_id)
      AND (@queue_state::text IS NULL OR @queue_state::text = 'Requested')

    UNION ALL

    SELECT
        cr.request_id AS request_id,
        cr.request_display AS request_display,
        cr.subject AS subject,
        cr.requested_at_utc AS requested_at_utc,
        (@as_of::date - cr.requested_at_utc)::int AS aging_days,
        cr.building_id AS building_id,
        cr.building_display AS building_display,
        cr.property_id AS property_id,
        cr.property_display AS property_display,
        cr.category_id AS category_id,
        cr.category_display AS category_display,
        cr.priority AS priority,
        cr.requested_by_party_id AS requested_by_party_id,
        cr.requested_by_display AS requested_by_display,
        owo.work_order_id AS work_order_id,
        owo.work_order_display AS work_order_display,
        owo.assigned_party_id AS assigned_party_id,
        owo.assigned_party_display AS assigned_party_display,
        owo.due_by_utc AS due_by_utc,
        CASE
            WHEN owo.due_by_utc IS NOT NULL AND owo.due_by_utc < @as_of THEN 'Overdue'
            ELSE 'WorkOrdered'
        END AS queue_state
    FROM candidate_requests cr
    JOIN open_work_orders owo
      ON owo.request_id = cr.request_id
    WHERE @queue_state::text IS NULL
       OR (@queue_state::text = 'WorkOrdered' AND (owo.due_by_utc IS NULL OR owo.due_by_utc >= @as_of))
       OR (@queue_state::text = 'Overdue' AND owo.due_by_utc IS NOT NULL AND owo.due_by_utc < @as_of)
)
""";

    private static readonly string CountSql = QueueCte + """
SELECT COUNT(*)::int
FROM queue_rows;
""";

    private static readonly string PageSql = QueueCte + """
SELECT
    request_id AS RequestId,
    request_display AS RequestDisplay,
    subject AS Subject,
    requested_at_utc AS RequestedAtUtc,
    aging_days AS AgingDays,
    building_id AS BuildingId,
    building_display AS BuildingDisplay,
    property_id AS PropertyId,
    property_display AS PropertyDisplay,
    category_id AS CategoryId,
    category_display AS CategoryDisplay,
    priority AS Priority,
    requested_by_party_id AS RequestedByPartyId,
    requested_by_display AS RequestedByDisplay,
    work_order_id AS WorkOrderId,
    work_order_display AS WorkOrderDisplay,
    assigned_party_id AS AssignedPartyId,
    assigned_party_display AS AssignedPartyDisplay,
    due_by_utc AS DueByUtc,
    queue_state AS QueueState
FROM queue_rows
ORDER BY requested_at_utc DESC, request_id DESC, work_order_id NULLS FIRST
OFFSET @offset
LIMIT @limit;
""";

    public async Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default)
    {
        query.EnsureInvariant();
        await uow.EnsureConnectionOpenAsync(ct);

        await ValidateBuildingFilterAsync(query.BuildingId, ct);
        await ValidatePropertyFilterAsync(query.PropertyId, ct);
        await ValidateCategoryFilterAsync(query.CategoryId, ct);
        await ValidateAssignedPartyFilterAsync(query.AssignedPartyId, ct);

        var parameters = new
        {
            as_of = query.AsOfUtc,
            building_id = query.BuildingId,
            property_id = query.PropertyId,
            category_id = query.CategoryId,
            assigned_party_id = query.AssignedPartyId,
            priority = query.Priority,
            queue_state = query.QueueState?.ToCode(),
            posted = (int)DocumentStatus.Posted,
            offset = query.Offset,
            limit = query.Limit
        };

        var total = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            CountSql,
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct));

        IReadOnlyList<MaintenanceQueueRow> rows;
        if (query.Offset >= total)
        {
            rows = [];
        }
        else
        {
            var dbRows = await uow.Connection.QueryAsync<PageRow>(new CommandDefinition(
                PageSql,
                parameters,
                transaction: uow.Transaction,
                cancellationToken: ct));

            rows = dbRows.Select(MapRow).ToArray();
        }

        var result = new MaintenanceQueuePage(rows, total);
        result.EnsureInvariant();
        return result;
    }

    private static MaintenanceQueueRow MapRow(PageRow row)
    {
        if (!MaintenanceQueueStateExtensions.TryParse(row.QueueState, out var queueState))
            throw new NgbInvariantViolationException(
                "Maintenance queue reader returned an unknown queue state.",
                context: new Dictionary<string, object?>
                {
                    ["queueState"] = row.QueueState,
                    ["requestId"] = row.RequestId,
                    ["workOrderId"] = row.WorkOrderId
                });

        var result = new MaintenanceQueueRow(
            RequestId: row.RequestId,
            RequestDisplay: row.RequestDisplay,
            Subject: row.Subject,
            RequestedAtUtc: row.RequestedAtUtc,
            AgingDays: row.AgingDays,
            BuildingId: row.BuildingId,
            BuildingDisplay: row.BuildingDisplay,
            PropertyId: row.PropertyId,
            PropertyDisplay: row.PropertyDisplay,
            CategoryId: row.CategoryId,
            CategoryDisplay: row.CategoryDisplay,
            Priority: row.Priority,
            RequestedByPartyId: row.RequestedByPartyId,
            RequestedByDisplay: row.RequestedByDisplay,
            WorkOrderId: row.WorkOrderId,
            WorkOrderDisplay: row.WorkOrderDisplay,
            AssignedPartyId: row.AssignedPartyId,
            AssignedPartyDisplay: row.AssignedPartyDisplay,
            DueByUtc: row.DueByUtc,
            QueueState: queueState);

        result.EnsureInvariant();
        return result;
    }

    private async Task ValidateBuildingFilterAsync(Guid? buildingId, CancellationToken ct)
    {
        if (buildingId is null)
            return;

        if (buildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a valid Building.");

        const string sql = """
SELECT
    p.kind AS Kind,
    c.is_deleted AS IsDeleted
FROM catalogs c
JOIN cat_pm_property p ON p.catalog_id = c.id
WHERE c.catalog_code = @code
  AND c.id = @building_id;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<PropertyFilterRow>(new CommandDefinition(
            sql,
            new { code = PropertyCode, building_id = buildingId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (row is null || row.IsDeleted || !string.Equals(row.Kind, "Building", StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(nameof(buildingId), "Select a valid Building.");
    }

    private async Task ValidatePropertyFilterAsync(Guid? propertyId, CancellationToken ct)
    {
        if (propertyId is null)
            return;

        if (propertyId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(propertyId), "Select a valid Property.");

        const string sql = """
SELECT c.is_deleted AS IsDeleted
FROM catalogs c
JOIN cat_pm_property p ON p.catalog_id = c.id
WHERE c.catalog_code = @code
  AND c.id = @property_id;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<DeletedFilterRow>(new CommandDefinition(
            sql,
            new { code = PropertyCode, property_id = propertyId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (row is null || row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(propertyId), "Select a valid Property.");
    }

    private async Task ValidateCategoryFilterAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is null)
            return;

        if (categoryId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(categoryId), "Select a valid Category.");

        const string sql = """
SELECT c.is_deleted AS IsDeleted
FROM catalogs c
WHERE c.catalog_code = @code
  AND c.id = @category_id;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<DeletedFilterRow>(new CommandDefinition(
            sql,
            new { code = MaintenanceCategoryCode, category_id = categoryId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (row is null || row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(categoryId), "Select a valid Category.");
    }

    private async Task ValidateAssignedPartyFilterAsync(Guid? assignedPartyId, CancellationToken ct)
    {
        if (assignedPartyId is null)
            return;

        if (assignedPartyId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(assignedPartyId), "Select a valid Assigned To.");

        const string sql = """
SELECT c.is_deleted AS IsDeleted
FROM catalogs c
WHERE c.catalog_code = @code
  AND c.id = @party_id;
""";

        var row = await uow.Connection.QuerySingleOrDefaultAsync<DeletedFilterRow>(new CommandDefinition(
            sql,
            new { code = PartyCode, party_id = assignedPartyId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (row is null || row.IsDeleted)
            throw new NgbArgumentInvalidException(nameof(assignedPartyId), "Select a valid Assigned To.");
    }

    private sealed record PropertyFilterRow(string Kind, bool IsDeleted);

    private sealed record DeletedFilterRow(bool IsDeleted);

    private sealed record PageRow(
        Guid RequestId,
        string RequestDisplay,
        string Subject,
        DateOnly RequestedAtUtc,
        int AgingDays,
        Guid BuildingId,
        string BuildingDisplay,
        Guid PropertyId,
        string PropertyDisplay,
        Guid CategoryId,
        string CategoryDisplay,
        string Priority,
        Guid RequestedByPartyId,
        string RequestedByDisplay,
        Guid? WorkOrderId,
        string? WorkOrderDisplay,
        Guid? AssignedPartyId,
        string? AssignedPartyDisplay,
        DateOnly? DueByUtc,
        string QueueState);
}
