using Dapper;
using NGB.Core.Documents;
using NGB.Contracts.Common;
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

    private static string BuildPageSql(bool knownTotal) => QueueCte + (knownTotal
        ? """
,
stats AS (
    SELECT @known_total::int AS total_count
),
"""
        : """
,
stats AS (
    SELECT COUNT(*)::int AS total_count
    FROM queue_rows
),
""") + """
paged AS (
SELECT
    *
FROM queue_rows
ORDER BY requested_at_utc DESC, request_id DESC, work_order_id NULLS FIRST
OFFSET @offset
LIMIT @limit
)
SELECT
    paged.request_id AS RequestId,
    paged.request_display AS RequestDisplay,
    paged.subject AS Subject,
    paged.requested_at_utc AS RequestedAtUtc,
    paged.aging_days AS AgingDays,
    paged.building_id AS BuildingId,
    paged.building_display AS BuildingDisplay,
    paged.property_id AS PropertyId,
    paged.property_display AS PropertyDisplay,
    paged.category_id AS CategoryId,
    paged.category_display AS CategoryDisplay,
    paged.priority AS Priority,
    paged.requested_by_party_id AS RequestedByPartyId,
    paged.requested_by_display AS RequestedByDisplay,
    paged.work_order_id AS WorkOrderId,
    paged.work_order_display AS WorkOrderDisplay,
    paged.assigned_party_id AS AssignedPartyId,
    paged.assigned_party_display AS AssignedPartyDisplay,
    paged.due_by_utc AS DueByUtc,
    paged.queue_state AS QueueState,
    (paged.request_id IS NOT NULL) AS HasRow,
    stats.total_count AS TotalCount
FROM stats
LEFT JOIN paged ON TRUE
ORDER BY paged.requested_at_utc DESC, paged.request_id DESC, paged.work_order_id NULLS FIRST;
""";

    public async Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default)
        => await GetPageCoreAsync(query, null, false, ct);

    public async Task<MaintenanceQueuePage> GetCursorPageAsync(
        MaintenanceQueueQuery query,
        MaintenanceQueuePageCursor? cursor,
        CancellationToken ct = default)
        => await GetPageCoreAsync(query with { Offset = cursor?.Offset ?? 0 }, cursor, true, ct);

    private async Task<MaintenanceQueuePage> GetPageCoreAsync(
        MaintenanceQueueQuery query,
        MaintenanceQueuePageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        query.EnsureInvariant();
        await uow.EnsureConnectionOpenAsync(ct);

        if (cursor is null)
            await ValidateFiltersAsync(query, ct);

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
            offset = PagingLimits.BoundOffset(query.Offset),
            limit = cursorPaging && query.Limit < int.MaxValue ? query.Limit + 1 : query.Limit,
            known_total = cursor?.Total
        };

        var dbRows = (await uow.Connection.QueryAsync<CombinedRow>(new CommandDefinition(
            BuildPageSql(cursor is not null),
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var total = dbRows[0].TotalCount;
        var dataRows = dbRows.Where(static row => row.HasRow).ToArray();
        var hasMore = cursorPaging && dataRows.Length > query.Limit;
        var rows = dataRows
            .Take(query.Limit)
            .Select(row => MapRow(new PageRow(
                row.RequestId!.Value,
                row.RequestDisplay!,
                row.Subject!,
                row.RequestedAtUtc!.Value,
                row.AgingDays!.Value,
                row.BuildingId!.Value,
                row.BuildingDisplay!,
                row.PropertyId!.Value,
                row.PropertyDisplay!,
                row.CategoryId!.Value,
                row.CategoryDisplay!,
                row.Priority!,
                row.RequestedByPartyId!.Value,
                row.RequestedByDisplay!,
                row.WorkOrderId,
                row.WorkOrderDisplay,
                row.AssignedPartyId,
                row.AssignedPartyDisplay,
                row.DueByUtc,
                row.QueueState!)))
            .ToArray();

        var result = new MaintenanceQueuePage(rows, total, hasMore);
        result.EnsureInvariant();
        return result;
    }

    internal static MaintenanceQueueRow MapRow(PageRow row)
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

    private async Task ValidateFiltersAsync(MaintenanceQueueQuery query, CancellationToken ct)
    {
        if (query.BuildingId == Guid.Empty)
            throw new NgbArgumentInvalidException("buildingId", "Select a valid Building.");

        if (query.PropertyId == Guid.Empty)
            throw new NgbArgumentInvalidException("propertyId", "Select a valid Property.");

        if (query.CategoryId == Guid.Empty)
            throw new NgbArgumentInvalidException("categoryId", "Select a valid Category.");

        if (query.AssignedPartyId == Guid.Empty)
            throw new NgbArgumentInvalidException("assignedPartyId", "Select a valid Assigned To.");

        if (query.BuildingId is null && query.PropertyId is null && query.CategoryId is null && query.AssignedPartyId is null)
            return;

        const string sql = """
SELECT
    @building_id::uuid IS NULL OR EXISTS (
        SELECT 1
        FROM catalogs c
        JOIN cat_pm_property p ON p.catalog_id = c.id
        WHERE c.catalog_code = @property_code
          AND c.id = @building_id::uuid
          AND c.is_deleted = FALSE
          AND LOWER(p.kind) = 'building'
    ) AS BuildingValid,
    @property_id::uuid IS NULL OR EXISTS (
        SELECT 1
        FROM catalogs c
        JOIN cat_pm_property p ON p.catalog_id = c.id
        WHERE c.catalog_code = @property_code
          AND c.id = @property_id::uuid
          AND c.is_deleted = FALSE
    ) AS PropertyValid,
    @category_id::uuid IS NULL OR EXISTS (
        SELECT 1
        FROM catalogs c
        WHERE c.catalog_code = @category_code
          AND c.id = @category_id::uuid
          AND c.is_deleted = FALSE
    ) AS CategoryValid,
    @assigned_party_id::uuid IS NULL OR EXISTS (
        SELECT 1
        FROM catalogs c
        WHERE c.catalog_code = @party_code
          AND c.id = @assigned_party_id::uuid
          AND c.is_deleted = FALSE
    ) AS AssignedPartyValid;
""";

        var validation = await uow.Connection.QuerySingleAsync<FilterValidationRow>(new CommandDefinition(
            sql,
            new
            {
                building_id = query.BuildingId,
                property_id = query.PropertyId,
                category_id = query.CategoryId,
                assigned_party_id = query.AssignedPartyId,
                property_code = PropertyCode,
                category_code = MaintenanceCategoryCode,
                party_code = PartyCode
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (!validation.BuildingValid)
            throw new NgbArgumentInvalidException("buildingId", "Select a valid Building.");

        if (!validation.PropertyValid)
            throw new NgbArgumentInvalidException("propertyId", "Select a valid Property.");

        if (!validation.CategoryValid)
            throw new NgbArgumentInvalidException("categoryId", "Select a valid Category.");

        if (!validation.AssignedPartyValid)
            throw new NgbArgumentInvalidException("assignedPartyId", "Select a valid Assigned To.");
    }

    internal async Task ValidateBuildingFilterAsync(Guid? buildingId, CancellationToken ct)
    {
        if (buildingId is null)
            return;

        if (buildingId.Value == Guid.Empty)
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

    internal async Task ValidatePropertyFilterAsync(Guid? propertyId, CancellationToken ct)
    {
        if (propertyId is null)
            return;

        if (propertyId.Value == Guid.Empty)
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

    internal async Task ValidateCategoryFilterAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is null)
            return;

        if (categoryId.Value == Guid.Empty)
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

    internal async Task ValidateAssignedPartyFilterAsync(Guid? assignedPartyId, CancellationToken ct)
    {
        if (assignedPartyId is null)
            return;

        if (assignedPartyId.Value == Guid.Empty)
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

    private sealed record FilterValidationRow(
        bool BuildingValid,
        bool PropertyValid,
        bool CategoryValid,
        bool AssignedPartyValid);

    internal sealed record PageRow(
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

    private sealed record CombinedRow(
        Guid? RequestId,
        string? RequestDisplay,
        string? Subject,
        DateOnly? RequestedAtUtc,
        int? AgingDays,
        Guid? BuildingId,
        string? BuildingDisplay,
        Guid? PropertyId,
        string? PropertyDisplay,
        Guid? CategoryId,
        string? CategoryDisplay,
        string? Priority,
        Guid? RequestedByPartyId,
        string? RequestedByDisplay,
        Guid? WorkOrderId,
        string? WorkOrderDisplay,
        Guid? AssignedPartyId,
        string? AssignedPartyDisplay,
        DateOnly? DueByUtc,
        string? QueueState,
        bool HasRow,
        int TotalCount);
}
