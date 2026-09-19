using System.Runtime.CompilerServices;
using Dapper;
using NGB.Core.Documents;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresMaintenanceQueueReader(IUnitOfWork uow) : IMaintenanceQueueReader, IMaintenanceQueueStreamReader
{
    private const string PropertyCode = PropertyManagementCodes.Property;
    private const string PartyCode = PropertyManagementCodes.Party;
    private const string MaintenanceCategoryCode = PropertyManagementCodes.MaintenanceCategory;

    private static string BuildQueueKeysCte(bool bounded = false, bool seek = false)
    {
        var requestedLimit = bounded ? "ORDER BY cr.requested_at_utc DESC, cr.request_id DESC LIMIT @limit" : "";
        var orderedLimit = bounded ? "ORDER BY cr.requested_at_utc DESC, cr.request_id DESC, wo.work_order_id LIMIT @limit" : "";
        string Requested(string predicate) => $"""
    (SELECT cr.request_id, cr.requested_at_utc, NULL::uuid AS work_order_id,
        NULL::uuid AS assigned_party_id, NULL::date AS due_by_utc, 'Requested'::text AS queue_state
    FROM candidate_requests cr
    WHERE @assigned_party_id::uuid IS NULL
      AND (@queue_state::text IS NULL OR @queue_state = 'Requested')
      AND NOT EXISTS (SELECT 1 FROM posted_work_orders pwo WHERE pwo.request_id = cr.request_id)
      {predicate}
      {requestedLimit})
""";
        string Ordered(string predicate) => $"""
    (SELECT cr.request_id, cr.requested_at_utc, wo.work_order_id, wo.assigned_party_id, wo.due_by_utc,
        CASE WHEN wo.due_by_utc < @as_of THEN 'Overdue' ELSE 'WorkOrdered' END
    FROM candidate_requests cr
    JOIN posted_work_orders wo ON wo.request_id = cr.request_id
    WHERE (@assigned_party_id::uuid IS NULL OR wo.assigned_party_id = @assigned_party_id)
      AND (@queue_state::text IS NULL
        OR (@queue_state = 'WorkOrdered' AND (wo.due_by_utc IS NULL OR wo.due_by_utc >= @as_of))
        OR (@queue_state = 'Overdue' AND wo.due_by_utc < @as_of))
      AND NOT EXISTS (
        SELECT 1 FROM doc_pm_work_order_completion wc
        JOIN documents wc_doc ON wc_doc.id = wc.document_id
          AND wc_doc.status = @posted AND wc_doc.type_code = 'pm.work_order_completion'
        WHERE wc.work_order_id = wo.work_order_id AND wc.closed_at_utc <= @as_of)
      {predicate}
      {orderedLimit})
""";
        // Disjoint ranges expose equality on a heavily repeated date to the planner. A tuple
        // inequality can underestimate that date's rows and sort the entire queue before LIMIT.
        const string olderDate = "AND cr.requested_at_utc < @after_requested_at_utc::date";
        const string sameDate = "AND cr.requested_at_utc = @after_requested_at_utc::date AND cr.request_id < @after_request_id::uuid";
        const string sameRequest = """
AND cr.requested_at_utc = @after_requested_at_utc::date AND cr.request_id = @after_request_id::uuid
AND (@after_work_order_id::uuid IS NULL OR wo.work_order_id > @after_work_order_id::uuid)
""";
        var branches = seek
            ? new[] { Requested(olderDate), Requested(sameDate), Ordered(olderDate), Ordered(sameDate), Ordered(sameRequest) }
            : new[] { Requested(""), Ordered("") };
        return $"""
WITH candidate_requests AS NOT MATERIALIZED (
    SELECT mr.document_id AS request_id, mr.requested_at_utc
    FROM doc_pm_maintenance_request mr
    JOIN documents req_doc ON req_doc.id = mr.document_id
      AND req_doc.status = @posted AND req_doc.type_code = 'pm.maintenance_request'
    JOIN cat_pm_property req_prop ON req_prop.catalog_id = mr.property_id
    WHERE mr.requested_at_utc <= @as_of
      AND (@property_id::uuid IS NULL OR mr.property_id = @property_id)
      AND (@building_id::uuid IS NULL OR CASE WHEN req_prop.kind = 'Building'
           THEN req_prop.catalog_id ELSE req_prop.parent_property_id END = @building_id)
      AND (@category_id::uuid IS NULL OR mr.category_id = @category_id)
      AND (@priority::text IS NULL OR mr.priority = @priority)
),
posted_work_orders AS NOT MATERIALIZED (
    SELECT wo.document_id AS work_order_id, wo.request_id, wo.assigned_party_id, wo.due_by_utc
    FROM doc_pm_work_order wo
    JOIN documents wo_doc ON wo_doc.id = wo.document_id
      AND wo_doc.status = @posted AND wo_doc.type_code = 'pm.work_order'
),
queue_keys AS NOT MATERIALIZED (
{string.Join("\n    UNION ALL\n", branches)}
)
""";
    }

    // Display joins happen after selecting page keys; full exports execute this projection once.
    private static string ProjectRows(string source) => $"""
SELECT q.request_id, mr.display AS request_display, mr.subject, q.requested_at_utc,
    (@as_of::date - q.requested_at_utc)::int AS aging_days,
    CASE WHEN p.kind = 'Building' THEN p.catalog_id ELSE p.parent_property_id END AS building_id,
    COALESCE(NULLIF(BTRIM(CASE WHEN p.kind = 'Building' THEN p.display ELSE b.display END), ''), '[Building]') AS building_display,
    mr.property_id, COALESCE(NULLIF(BTRIM(p.display), ''), '[Property]') AS property_display,
    mr.category_id, COALESCE(NULLIF(BTRIM(c.display), ''), '[Category]') AS category_display,
    mr.priority, mr.party_id AS requested_by_party_id,
    COALESCE(NULLIF(BTRIM(rp.display), ''), '[Party]') AS requested_by_display,
    q.work_order_id, wo.display AS work_order_display, q.assigned_party_id,
    CASE WHEN q.work_order_id IS NULL THEN NULL ELSE COALESCE(NULLIF(BTRIM(ap.display), ''), '[Party]') END AS assigned_party_display,
    q.due_by_utc, q.queue_state
FROM {source} q
JOIN doc_pm_maintenance_request mr ON mr.document_id = q.request_id
JOIN cat_pm_property p ON p.catalog_id = mr.property_id
LEFT JOIN cat_pm_property b ON b.catalog_id = p.parent_property_id
LEFT JOIN cat_pm_maintenance_category c ON c.catalog_id = mr.category_id
LEFT JOIN cat_pm_party rp ON rp.catalog_id = mr.party_id
LEFT JOIN doc_pm_work_order wo ON wo.document_id = q.work_order_id
LEFT JOIN cat_pm_party ap ON ap.catalog_id = q.assigned_party_id
""";

    private static string QueueCte => BuildQueueKeysCte() + ", queue_rows AS (" + ProjectRows("queue_keys") + ")";

    private static string BuildPageSql(bool knownTotal, bool useSeek, bool includeTotal = true)
    {
        var statsSql = knownTotal || !includeTotal
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
    FROM queue_keys
),
""";
        var sourceSql = useSeek
            ? """
seek_rows AS (
    SELECT *
    FROM queue_keys
    WHERE requested_at_utc < @after_requested_at_utc::date
       OR (requested_at_utc = @after_requested_at_utc::date AND request_id < @after_request_id::uuid)
       OR (requested_at_utc = @after_requested_at_utc::date AND request_id = @after_request_id::uuid AND (
            (@after_work_order_id::uuid IS NULL AND work_order_id IS NOT NULL)
            OR (@after_work_order_id::uuid IS NOT NULL AND work_order_id IS NOT NULL AND work_order_id > @after_work_order_id::uuid)
       ))
),
"""
            : string.Empty;
        var sourceName = useSeek ? "seek_rows" : "queue_keys";
        var offsetSql = useSeek ? string.Empty : "OFFSET @offset";

        return BuildQueueKeysCte(bounded: !includeTotal && !knownTotal, seek: !includeTotal && !knownTotal && useSeek) + statsSql + sourceSql + $"""
paged_keys AS MATERIALIZED (
SELECT
    *
FROM {sourceName}
ORDER BY requested_at_utc DESC, request_id DESC, work_order_id NULLS FIRST
{offsetSql}
LIMIT @limit
),
paged AS (
{ProjectRows("paged_keys")}
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
    }

    public async IAsyncEnumerable<IReadOnlyList<MaintenanceQueueRow>> ReadAsync(
        MaintenanceQueueQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        query.EnsureInvariant();

        await uow.EnsureConnectionOpenAsync(ct);
        await ValidateFiltersAsync(query, ct);

        var sql = QueueCte + """
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
    paged.queue_state AS QueueState
FROM queue_rows paged
ORDER BY paged.requested_at_utc DESC, paged.request_id DESC, paged.work_order_id NULLS FIRST;
""";
        var args = new
        {
            as_of = query.AsOfUtc, building_id = query.BuildingId, property_id = query.PropertyId,
            category_id = query.CategoryId, assigned_party_id = query.AssignedPartyId,
            priority = query.Priority, queue_state = query.QueueState?.ToCode(), posted = (int)DocumentStatus.Posted
        };

        await foreach (var batch in PostgresReportCursorStream.ReadAsync<PageRow>(uow, sql, args, ct))
        {
            yield return batch.Select(MapRow).ToArray();
        }
    }

    public async Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default)
        => await GetPageCoreAsync(query, null, false, ct);

    public async Task<MaintenanceQueuePage> GetCursorPageAsync(
        MaintenanceQueueQuery query,
        MaintenanceQueuePageCursor? cursor,
        CancellationToken ct = default)
        => await GetPageCoreAsync(query with { Offset = cursor?.Offset ?? 0 }, cursor, true, ct);

    public async Task<MaintenanceQueueDashboard> GetDashboardAsync(
        DateOnly asOfUtc,
        int itemLimit,
        CancellationToken ct = default)
    {
        if (itemLimit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(itemLimit), itemLimit, "Item limit must be positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = QueueCte + """
,
stats AS (
    SELECT
        COUNT(*)::integer AS total_count,
        COUNT(*) FILTER (WHERE queue_state = 'Overdue')::integer AS overdue_count,
        COUNT(*) FILTER (WHERE aging_days <= 3)::integer AS days_0_to_3,
        COUNT(*) FILTER (WHERE aging_days BETWEEN 4 AND 7)::integer AS days_4_to_7,
        COUNT(*) FILTER (WHERE aging_days BETWEEN 8 AND 14)::integer AS days_8_to_14,
        COUNT(*) FILTER (WHERE aging_days >= 15)::integer AS days_15_plus
    FROM queue_rows
),
ranked AS (
    SELECT
        queue_rows.*,
        ROW_NUMBER() OVER (
            ORDER BY
                CASE queue_state WHEN 'Overdue' THEN 0 WHEN 'WorkOrdered' THEN 1 ELSE 2 END,
                aging_days DESC,
                requested_at_utc DESC,
                request_id DESC,
                work_order_id NULLS FIRST) AS row_number
    FROM queue_rows
)
SELECT
    ranked.request_id AS RequestId,
    ranked.request_display AS RequestDisplay,
    ranked.subject AS Subject,
    ranked.requested_at_utc AS RequestedAtUtc,
    ranked.aging_days AS AgingDays,
    ranked.building_id AS BuildingId,
    ranked.building_display AS BuildingDisplay,
    ranked.property_id AS PropertyId,
    ranked.property_display AS PropertyDisplay,
    ranked.category_id AS CategoryId,
    ranked.category_display AS CategoryDisplay,
    ranked.priority AS Priority,
    ranked.requested_by_party_id AS RequestedByPartyId,
    ranked.requested_by_display AS RequestedByDisplay,
    ranked.work_order_id AS WorkOrderId,
    ranked.work_order_display AS WorkOrderDisplay,
    ranked.assigned_party_id AS AssignedPartyId,
    ranked.assigned_party_display AS AssignedPartyDisplay,
    ranked.due_by_utc AS DueByUtc,
    ranked.queue_state AS QueueState,
    (ranked.request_id IS NOT NULL) AS HasRow,
    stats.total_count AS TotalCount,
    stats.overdue_count AS OverdueCount,
    stats.days_0_to_3 AS Days0To3,
    stats.days_4_to_7 AS Days4To7,
    stats.days_8_to_14 AS Days8To14,
    stats.days_15_plus AS Days15Plus
FROM stats
LEFT JOIN ranked ON ranked.row_number <= @item_limit
ORDER BY ranked.row_number;
""";

        var dbRows = (await uow.Connection.QueryAsync<DashboardCombinedRow>(new CommandDefinition(
            sql,
            new
            {
                as_of = asOfUtc,
                building_id = (Guid?)null,
                property_id = (Guid?)null,
                category_id = (Guid?)null,
                assigned_party_id = (Guid?)null,
                priority = (string?)null,
                queue_state = (string?)null,
                posted = (int)DocumentStatus.Posted,
                item_limit = itemLimit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var stats = dbRows[0];
        var rows = dbRows
            .Where(static row => row.HasRow)
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

        return new MaintenanceQueueDashboard(
            stats.TotalCount,
            stats.OverdueCount,
            stats.Days0To3,
            stats.Days4To7,
            stats.Days8To14,
            stats.Days15Plus,
            rows);
    }

    private async Task<MaintenanceQueuePage> GetPageCoreAsync(
        MaintenanceQueueQuery query,
        MaintenanceQueuePageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        query.EnsureInvariant();
        cursorPaging |= !query.IncludeTotal;

        await uow.EnsureConnectionOpenAsync(ct);

        if (cursor is null)
            await ValidateFiltersAsync(query, ct);

        var useSeek = cursor is { AfterRequestedAtUtc: not null, AfterRequestId: not null };

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
            known_total = cursor?.Total,
            after_requested_at_utc = cursor?.AfterRequestedAtUtc,
            after_request_id = cursor?.AfterRequestId,
            after_work_order_id = cursor?.AfterWorkOrderId
        };

        var dbRows = (await uow.Connection.QueryAsync<CombinedRow>(new CommandDefinition(
            BuildPageSql(cursor?.Total is not null, useSeek, query.IncludeTotal),
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var total = dbRows[0].TotalCount;
        var dataRows = dbRows.Where(static row => row.HasRow).ToArray();
        var hasMore = cursorPaging && dataRows.Length > query.Limit;
        var visibleRows = dataRows.Take(query.Limit).ToArray();
        var rows = visibleRows
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

        var last = visibleRows.LastOrDefault();
        var result = new MaintenanceQueuePage(
            rows,
            total ?? (!hasMore && query.Offset == 0 ? rows.Length : null),
            hasMore,
            last?.RequestedAtUtc,
            last?.RequestId,
            last?.WorkOrderId);
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
        int? TotalCount);

    private sealed record DashboardCombinedRow(
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
        int TotalCount,
        int OverdueCount,
        int Days0To3,
        int Days4To7,
        int Days8To14,
        int Days15Plus);
}
