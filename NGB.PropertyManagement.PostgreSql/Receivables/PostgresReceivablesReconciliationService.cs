using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Paging;

namespace NGB.PropertyManagement.PostgreSql.Receivables;

/// <summary>
/// PostgreSQL implementation for receivables reconciliation:
/// AR (GL turnovers) vs Open Items (Operational Register movements).
///
/// Modes:
/// - Movement = net changes in the requested month range.
/// - Balance  = cutoff / month-end reconciliation as of ToMonthInclusive.
/// </summary>
public sealed class PostgresReceivablesReconciliationService(IUnitOfWork uow) : IReceivablesReconciliationService
{
    private static readonly Regex SafeTableCode = new("^[a-z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ReceivablesReconciliationReport> GetAsync(
        ReceivablesReconciliationRequest request,
        CancellationToken ct = default)
    {
        EnsureMonthStart(request.FromMonthInclusive, nameof(request.FromMonthInclusive), "From month");
        EnsureMonthStart(request.ToMonthInclusive, nameof(request.ToMonthInclusive), "To month");

        if (request.ToMonthInclusive < request.FromMonthInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToMonthInclusive), request.ToMonthInclusive, "To month must be on or after From month.");

        if (request.Offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(request.Offset), request.Offset, "Offset must be zero or greater.");

        if (request.Limit is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException(nameof(request.Limit), request.Limit, "Limit must be between 1 and 500.");

        if (!Enum.IsDefined(request.Status))
            throw new NgbArgumentInvalidException(nameof(request.Status), "Select a valid reconciliation status filter.");

        var requestedOffset = PagingLimits.BoundOffset(request.Offset);

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await ReadQueryContextAsync(ct);
        var policy = (context.ArAccountId, context.OpenItemsRegisterId);
        var tableCode = context.TableCode;
        var cursorKind = OpaqueCursorCodec.BuildKind(
            "pm.receivables.reconciliation",
            request.FromMonthInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.ToMonthInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ((int)request.Mode).ToString(CultureInfo.InvariantCulture),
            ((int)request.Status).ToString(CultureInfo.InvariantCulture),
            policy.ArAccountId.ToString("N"),
            policy.OpenItemsRegisterId.ToString("N"));
        var pageCursor = string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : OpaqueCursorCodec.Decode<ReceivablesPageCursor>(cursorKind, request.Cursor);
        var effectiveOffset = pageCursor?.NextOffset ?? requestedOffset;

        var movementsTable = $"opreg_{tableCode}__movements";
        var balancesTable = $"opreg_{tableCode}__balances";
        var movementsTableExists = context.MovementsTableExists;
        var balancesTableExists = context.BalancesTableExists;

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var (glSourceSql, oiSourceSql) = request.Mode switch
        {
            ReceivablesReconciliationMode.Movement => (
                BuildMovementGlSourceSql(),
                BuildMovementOiSourceSql(movementsTable, movementsTableExists)),
            ReceivablesReconciliationMode.Balance => (
                BuildBalanceGlSourceSql(),
                BuildBalanceOiSourceSql(movementsTable, movementsTableExists, balancesTable, balancesTableExists)),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };
        var statsSql = pageCursor is null
            ? """
              SELECT
                  COUNT(*)::integer AS total_row_count,
                  COUNT(*) FILTER (WHERE ar_net <> open_items_net)::integer AS total_mismatch_row_count,
                  COUNT(*) FILTER (WHERE ar_net <> 0 AND open_items_net = 0)::integer AS total_gl_only_row_count,
                  COUNT(*) FILTER (WHERE ar_net = 0 AND open_items_net <> 0)::integer AS total_open_items_only_row_count,
                  COALESCE(SUM(ar_net), 0) AS total_ar_net,
                  COALESCE(SUM(open_items_net), 0) AS total_open_items_net
              FROM reconciliation
              """
            : """
              SELECT
                  @KnownRowCount::integer AS total_row_count,
                  @KnownMismatchRowCount::integer AS total_mismatch_row_count,
                  @KnownGlOnlyRowCount::integer AS total_gl_only_row_count,
                  @KnownOpenItemsOnlyRowCount::integer AS total_open_items_only_row_count,
                  @KnownArNet::numeric AS total_ar_net,
                  @KnownOpenItemsNet::numeric AS total_open_items_net
              """;
        var seekPredicateSql = pageCursor is null
            ? string.Empty
            : "WHERE (party_id, property_id, lease_id) > (@AfterPartyId::uuid, @AfterPropertyId::uuid, @AfterLeaseId::uuid)";
        var offsetSql = pageCursor is null ? "OFFSET @Offset" : string.Empty;

        // IMPORTANT: movementsTable is interpolated (PostgreSQL doesn't allow binding identifiers).
        // It is safe because table_code is a generated column guarded by DB constraints (safe chars + length).
        var sql = $"""
WITH
{glSourceSql},
gl_agg AS (
    SELECT
        COALESCE(p.value_id, @Empty::uuid)   AS party_id,
        COALESCE(pr.value_id, @Empty::uuid)  AS property_id,
        COALESCE(l.value_id, @Empty::uuid)   AS lease_id,
        SUM(gl_source.net) AS ar_net
    FROM gl_source
    LEFT JOIN platform_dimension_set_items p
        ON p.dimension_set_id = gl_source.dimension_set_id AND p.dimension_id = @PartyDimId::uuid
    LEFT JOIN platform_dimension_set_items pr
        ON pr.dimension_set_id = gl_source.dimension_set_id AND pr.dimension_id = @PropertyDimId::uuid
    LEFT JOIN platform_dimension_set_items l
        ON l.dimension_set_id = gl_source.dimension_set_id AND l.dimension_id = @LeaseDimId::uuid
    GROUP BY 1,2,3
),
{oiSourceSql},
oi_agg AS (
    SELECT
        COALESCE(p.value_id, @Empty::uuid)   AS party_id,
        COALESCE(pr.value_id, @Empty::uuid)  AS property_id,
        COALESCE(l.value_id, @Empty::uuid)   AS lease_id,
        SUM(oi_source.net) AS open_items_net
    FROM oi_source
    LEFT JOIN platform_dimension_set_items p
        ON p.dimension_set_id = oi_source.dimension_set_id AND p.dimension_id = @PartyDimId::uuid
    LEFT JOIN platform_dimension_set_items pr
        ON pr.dimension_set_id = oi_source.dimension_set_id AND pr.dimension_id = @PropertyDimId::uuid
    LEFT JOIN platform_dimension_set_items l
        ON l.dimension_set_id = oi_source.dimension_set_id AND l.dimension_id = @LeaseDimId::uuid
    GROUP BY 1,2,3
),
reconciliation AS (
    SELECT
        COALESCE(gl_agg.party_id, oi_agg.party_id)        AS party_id,
        COALESCE(gl_agg.property_id, oi_agg.property_id)  AS property_id,
        COALESCE(gl_agg.lease_id, oi_agg.lease_id)        AS lease_id,
        COALESCE(gl_agg.ar_net, 0)                        AS ar_net,
        COALESCE(oi_agg.open_items_net, 0)                AS open_items_net
    FROM gl_agg
    FULL OUTER JOIN oi_agg
        ON gl_agg.party_id = oi_agg.party_id
       AND gl_agg.property_id = oi_agg.property_id
       AND gl_agg.lease_id = oi_agg.lease_id
    WHERE COALESCE(gl_agg.ar_net, 0) <> 0
       OR COALESCE(oi_agg.open_items_net, 0) <> 0
),
filtered_reconciliation AS (
    SELECT *
    FROM reconciliation
    WHERE @Status = 0
       OR (@Status = 1 AND ar_net = open_items_net)
       OR (@Status = 2 AND ar_net <> open_items_net)
       OR (@Status = 3 AND ar_net <> 0 AND open_items_net = 0)
       OR (@Status = 4 AND ar_net = 0 AND open_items_net <> 0)
),
stats AS (
    {statsSql}
),
paged AS (
    SELECT *
    FROM filtered_reconciliation
    {seekPredicateSql}
    ORDER BY party_id, property_id, lease_id
    {offsetSql}
    LIMIT @LimitPlusOne
)
SELECT
    paged.party_id AS PartyId,
    paged.property_id AS PropertyId,
    paged.lease_id AS LeaseId,
    COALESCE(paged.ar_net, 0) AS ArNet,
    COALESCE(paged.open_items_net, 0) AS OpenItemsNet,
    party_head.display AS PartyDisplay,
    property_head.display AS PropertyDisplay,
    lease_head.display AS LeaseDisplay,
    (paged.party_id IS NOT NULL) AS HasRow,
    stats.total_row_count AS TotalRowCount,
    stats.total_mismatch_row_count AS TotalMismatchRowCount,
    stats.total_gl_only_row_count AS TotalGlOnlyRowCount,
    stats.total_open_items_only_row_count AS TotalOpenItemsOnlyRowCount,
    stats.total_ar_net AS TotalArNet,
    stats.total_open_items_net AS TotalOpenItemsNet
FROM stats
LEFT JOIN paged ON TRUE
LEFT JOIN catalogs party_catalog
    ON party_catalog.id = paged.party_id AND party_catalog.catalog_code = @PartyCatalogCode
LEFT JOIN cat_pm_party party_head ON party_head.catalog_id = party_catalog.id
LEFT JOIN catalogs property_catalog
    ON property_catalog.id = paged.property_id AND property_catalog.catalog_code = @PropertyCatalogCode
LEFT JOIN cat_pm_property property_head ON property_head.catalog_id = property_catalog.id
LEFT JOIN documents lease_document
    ON lease_document.id = paged.lease_id AND lease_document.type_code = @LeaseDocumentTypeCode
LEFT JOIN doc_pm_lease lease_head ON lease_head.document_id = lease_document.id
ORDER BY paged.party_id, paged.property_id, paged.lease_id;
""";

        var cmd = new CommandDefinition(
            sql,
            new
            {
                policy.ArAccountId,
                FromMonth = request.FromMonthInclusive,
                ToMonth = request.ToMonthInclusive,
                PartyDimId = partyDimId,
                PropertyDimId = propertyDimId,
                LeaseDimId = leaseDimId,
                PartyCatalogCode = PropertyManagementCodes.Party,
                PropertyCatalogCode = PropertyManagementCodes.Property,
                LeaseDocumentTypeCode = PropertyManagementCodes.Lease,
                Offset = requestedOffset,
                Status = (int)request.Status,
                LimitPlusOne = request.Limit + 1,
                AfterPartyId = pageCursor?.AfterPartyId,
                AfterPropertyId = pageCursor?.AfterPropertyId,
                AfterLeaseId = pageCursor?.AfterLeaseId,
                KnownRowCount = pageCursor?.TotalRowCount,
                KnownMismatchRowCount = pageCursor?.TotalMismatchRowCount,
                KnownGlOnlyRowCount = pageCursor?.TotalGlOnlyRowCount,
                KnownOpenItemsOnlyRowCount = pageCursor?.TotalOpenItemsOnlyRowCount,
                KnownArNet = pageCursor?.TotalArNet,
                KnownOpenItemsNet = pageCursor?.TotalOpenItemsNet,
                Guid.Empty
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<RawRow>(cmd)).AsList();

        var stats = rows[0];
        var filteredRowCount = ResolveFilteredRowCount(
            request.Status,
            stats.TotalRowCount,
            stats.TotalMismatchRowCount,
            stats.TotalGlOnlyRowCount,
            stats.TotalOpenItemsOnlyRowCount);
        var pageRows = rows.Where(static row => row.HasRow).ToList();
        var hasMore = pageRows.Count > request.Limit;
        if (hasMore)
            pageRows.RemoveAt(pageRows.Count - 1);

        var nextCursor = hasMore && pageRows.Count > 0
            ? OpaqueCursorCodec.Encode(
                cursorKind,
                new ReceivablesPageCursor(
                    pageRows[^1].PartyId,
                    pageRows[^1].PropertyId,
                    pageRows[^1].LeaseId,
                    effectiveOffset + pageRows.Count,
                    stats.TotalRowCount,
                    stats.TotalMismatchRowCount,
                    stats.TotalGlOnlyRowCount,
                    stats.TotalOpenItemsOnlyRowCount,
                    stats.TotalArNet,
                    stats.TotalOpenItemsNet))
            : null;

        var resultRows = new List<ReceivablesReconciliationRow>(pageRows.Count);

        foreach (var r in pageRows)
        {
            var diff = r.ArNet - r.OpenItemsNet;
            var hasDiff = diff != 0m;
            var rowKind = ResolveRowKind(r.ArNet, r.OpenItemsNet, hasDiff);
            resultRows.Add(new ReceivablesReconciliationRow(
                PartyId: r.PartyId,
                PartyDisplay: r.PartyDisplay,
                PropertyId: r.PropertyId,
                PropertyDisplay: r.PropertyDisplay,
                LeaseId: r.LeaseId,
                LeaseDisplay: r.LeaseDisplay,
                ArNet: r.ArNet,
                OpenItemsNet: r.OpenItemsNet,
                Diff: diff,
                RowKind: rowKind,
                HasDiff: hasDiff));
        }

        return new ReceivablesReconciliationReport(
            request.FromMonthInclusive,
            request.ToMonthInclusive,
            request.Mode,
            policy.ArAccountId,
            policy.OpenItemsRegisterId,
            TotalArNet: stats.TotalArNet,
            TotalOpenItemsNet: stats.TotalOpenItemsNet,
            TotalDiff: stats.TotalArNet - stats.TotalOpenItemsNet,
            RowCount: stats.TotalRowCount,
            MismatchRowCount: stats.TotalMismatchRowCount,
            Rows: resultRows,
            Offset: effectiveOffset,
            Limit: request.Limit,
            HasMore: hasMore,
            NextCursor: nextCursor,
            FilteredRowCount: filteredRowCount,
            GlOnlyRowCount: stats.TotalGlOnlyRowCount,
            OpenItemsOnlyRowCount: stats.TotalOpenItemsOnlyRowCount);
    }

    private static string BuildMovementGlSourceSql() =>
        """
gl_source AS (
    SELECT
        t.dimension_set_id,
        SUM(t.debit_amount - t.credit_amount) AS net
    FROM accounting_turnovers t
    WHERE t.account_id = @ArAccountId::uuid
      AND t.period >= @FromMonth::date
      AND t.period <= @ToMonth::date
    GROUP BY t.dimension_set_id
)
""";

    internal static string BuildBalanceGlSourceSql() =>
        """
latest_closed AS (
    SELECT MAX(b.period) AS period
    FROM accounting_balances b
    WHERE b.account_id = @ArAccountId::uuid
      AND b.period <= @ToMonth::date
),
gl_seed AS (
    SELECT
        b.dimension_set_id,
        b.closing_balance AS net
    FROM accounting_balances b
    CROSS JOIN latest_closed lc
    WHERE lc.period IS NOT NULL
      AND b.account_id = @ArAccountId::uuid
      AND b.period = lc.period
),
gl_roll AS (
    SELECT
        t.dimension_set_id,
        SUM(t.debit_amount - t.credit_amount) AS net
    FROM accounting_turnovers t
    CROSS JOIN latest_closed lc
    WHERE t.account_id = @ArAccountId::uuid
      AND t.period <= @ToMonth::date
      AND (lc.period IS NULL OR t.period > lc.period)
    GROUP BY t.dimension_set_id
),
gl_source AS (
    SELECT
        s.dimension_set_id,
        SUM(s.net) AS net
    FROM (
        SELECT dimension_set_id, net FROM gl_seed
        UNION ALL
        SELECT dimension_set_id, net FROM gl_roll
    ) s
    GROUP BY s.dimension_set_id
)
""";

    internal static string BuildMovementOiSourceSql(string movementsTable, bool movementsTableExists)
        => movementsTableExists
            ? $"""
oi_source AS (
    SELECT
        m.dimension_set_id,
        SUM(CASE WHEN m.is_storno THEN -m.amount ELSE m.amount END) AS net
    FROM {movementsTable} m
    WHERE m.period_month >= @FromMonth::date
      AND m.period_month <= @ToMonth::date
    GROUP BY m.dimension_set_id
)
"""
            : BuildEmptyOiSourceSql();

    internal static string BuildBalanceOiSourceSql(string movementsTable, bool movementsTableExists)
        => BuildBalanceOiSourceSql(movementsTable, movementsTableExists, string.Empty, balancesTableExists: false);

    internal static string BuildBalanceOiSourceSql(
        string movementsTable,
        bool movementsTableExists,
        string balancesTable,
        bool balancesTableExists)
        => movementsTableExists
            ? balancesTableExists
                ? $"""
oi_latest_snapshot AS (
    SELECT MAX(period_month) AS period_month
    FROM {balancesTable}
    WHERE period_month <= @ToMonth::date
),
oi_seed AS (
    SELECT b.dimension_set_id, b.amount AS net
    FROM {balancesTable} b
    CROSS JOIN oi_latest_snapshot latest
    WHERE b.period_month = latest.period_month
),
oi_roll AS (
    SELECT
        m.dimension_set_id,
        SUM(CASE WHEN m.is_storno THEN -m.amount ELSE m.amount END) AS net
    FROM {movementsTable} m
    CROSS JOIN oi_latest_snapshot latest
    WHERE m.period_month <= @ToMonth::date
      AND (latest.period_month IS NULL OR m.period_month > latest.period_month)
    GROUP BY m.dimension_set_id
),
oi_source AS (
    SELECT source.dimension_set_id, SUM(source.net) AS net
    FROM (
        SELECT dimension_set_id, net FROM oi_seed
        UNION ALL
        SELECT dimension_set_id, net FROM oi_roll
    ) source
    GROUP BY source.dimension_set_id
)
"""
                : $"""
oi_source AS (
    SELECT
        m.dimension_set_id,
        SUM(CASE WHEN m.is_storno THEN -m.amount ELSE m.amount END) AS net
    FROM {movementsTable} m
    WHERE m.period_month <= @ToMonth::date
    GROUP BY m.dimension_set_id
)
"""
            : BuildEmptyOiSourceSql();

    internal static string BuildEmptyOiSourceSql() =>
        """
oi_source AS (
    SELECT
        NULL::uuid AS dimension_set_id,
        NULL::numeric AS net
    WHERE FALSE
)
""";

    private sealed record RawRow(
        Guid PartyId,
        Guid PropertyId,
        Guid LeaseId,
        decimal ArNet,
        decimal OpenItemsNet,
        string? PartyDisplay,
        string? PropertyDisplay,
        string? LeaseDisplay,
        bool HasRow,
        int TotalRowCount,
        int TotalMismatchRowCount,
        int TotalGlOnlyRowCount,
        int TotalOpenItemsOnlyRowCount,
        decimal TotalArNet,
        decimal TotalOpenItemsNet);

    private sealed record ReceivablesPageCursor(
        Guid AfterPartyId,
        Guid AfterPropertyId,
        Guid AfterLeaseId,
        int NextOffset,
        int TotalRowCount,
        int TotalMismatchRowCount,
        int TotalGlOnlyRowCount,
        int TotalOpenItemsOnlyRowCount,
        decimal TotalArNet,
        decimal TotalOpenItemsNet);

    internal static ReceivablesReconciliationRowKind ResolveRowKind(decimal arNet, decimal openItemsNet, bool hasDiff)
    {
        if (arNet != 0m && openItemsNet == 0m)
            return ReceivablesReconciliationRowKind.GlOnly;

        if (arNet == 0m && openItemsNet != 0m)
            return ReceivablesReconciliationRowKind.OpenItemsOnly;

        return hasDiff
            ? ReceivablesReconciliationRowKind.Mismatch
            : ReceivablesReconciliationRowKind.Matched;
    }

    internal static int ResolveFilteredRowCount(
        ReceivablesReconciliationStatusFilter status,
        int rowCount,
        int mismatchRowCount,
        int glOnlyRowCount,
        int openItemsOnlyRowCount)
        => status switch
        {
            ReceivablesReconciliationStatusFilter.All => rowCount,
            ReceivablesReconciliationStatusFilter.Matched => rowCount - mismatchRowCount,
            ReceivablesReconciliationStatusFilter.Mismatch => mismatchRowCount,
            ReceivablesReconciliationStatusFilter.GlOnly => glOnlyRowCount,
            ReceivablesReconciliationStatusFilter.OpenItemsOnly => openItemsOnlyRowCount,
            _ => throw new NgbArgumentInvalidException(nameof(status), "Select a valid reconciliation status filter.")
        };

    private static void EnsureMonthStart(DateOnly month, string paramName, string label)
    {
        if (month.Day != 1)
            throw new NgbArgumentOutOfRangeException(paramName, month, $"{label} must be the first day of a month.");
    }

    internal static (Guid ArAccountId, Guid OpenItemsRegisterId) EnsureRequiredPolicyValues(
        Guid? arAccountId,
        Guid? openItemsRegisterId)
    {
        var requiredArAccountId = arAccountId.GetValueOrDefault();
        if (requiredArAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                "PM accounting policy has no ar_tenants_account_id configured.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy",
                    ["field"] = "ar_tenants_account_id"
                });
        }

        var requiredOpenItemsRegisterId = openItemsRegisterId.GetValueOrDefault();
        if (requiredOpenItemsRegisterId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                "PM accounting policy has no receivables_open_items_register_id configured.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy",
                    ["field"] = "receivables_open_items_register_id"
                });
        }

        return (requiredArAccountId, requiredOpenItemsRegisterId);
    }

    private async Task<QueryContext> ReadQueryContextAsync(CancellationToken ct)
    {
        const string sql = """
WITH policy AS (
    SELECT
        ar_tenants_account_id AS "ArAccountId",
        receivables_open_items_register_id AS "OpenItemsRegisterId"
    FROM cat_pm_accounting_policy
    LIMIT 2
)
SELECT
    policy."ArAccountId" AS "ArAccountId",
    policy."OpenItemsRegisterId" AS "OpenItemsRegisterId",
    registers.register_id AS "ResolvedRegisterId",
    registers.table_code AS "TableCode",
    to_regclass('opreg_' || registers.table_code || '__movements') IS NOT NULL AS "MovementsTableExists",
    to_regclass('opreg_' || registers.table_code || '__balances') IS NOT NULL AS "BalancesTableExists"
FROM policy
LEFT JOIN operational_registers registers
  ON registers.register_id = policy."OpenItemsRegisterId";
""";

        var rows = (await uow.Connection.QueryAsync<QueryContextRow>(
            new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct))).AsList();

        if (rows.Count == 0)
        {
            throw new NgbConfigurationViolationException(
                "PM accounting policy is missing.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy"
                });
        }

        if (rows.Count > 1)
        {
            throw new NgbConfigurationViolationException(
                "Multiple pm.accounting_policy records exist. Expected a single record.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy",
                    ["actualCount"] = rows.Count
                });
        }

        var row = rows[0];
        var (arAccountId, registerId) = EnsureRequiredPolicyValues(row.ArAccountId, row.OpenItemsRegisterId);

        if (!row.ResolvedRegisterId.HasValue)
        {
            throw new NgbConfigurationViolationException(
                "Receivables open-items operational register does not exist.",
                new Dictionary<string, object?> { ["registerId"] = registerId });
        }

        return new QueryContext(
            arAccountId,
            registerId,
            EnsureSafeTableCode(row.TableCode, registerId),
            row.MovementsTableExists,
            row.BalancesTableExists);
    }

    private sealed record QueryContext(
        Guid ArAccountId,
        Guid OpenItemsRegisterId,
        string TableCode,
        bool MovementsTableExists,
        bool BalancesTableExists);

    private sealed record QueryContextRow(
        Guid? ArAccountId,
        Guid? OpenItemsRegisterId,
        Guid? ResolvedRegisterId,
        string? TableCode,
        bool MovementsTableExists,
        bool BalancesTableExists);

    internal static string EnsureSafeTableCode(string? rawTableCode, Guid registerId)
    {
        var tableCode = rawTableCode?.Trim();
        if (string.IsNullOrWhiteSpace(tableCode))
        {
            throw new NgbConfigurationViolationException(
                "Receivables open-items operational register has empty table_code.",
                new Dictionary<string, object?> { ["registerId"] = registerId });
        }

        if (!SafeTableCode.IsMatch(tableCode))
        {
            throw new NgbConfigurationViolationException(
                "Operational register table_code is not safe.",
                new Dictionary<string, object?> { ["registerId"] = registerId, ["tableCode"] = tableCode });
        }

        return tableCode;
    }
}
