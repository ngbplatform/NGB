using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Paging;

namespace NGB.PropertyManagement.PostgreSql.Payables;

/// <summary>
/// PostgreSQL implementation for payables reconciliation:
/// AP (GL turnovers) vs Open Items (Operational Register movements).
///
/// Modes:
/// - Movement = net changes in the requested month range.
/// - Balance  = cutoff / month-end reconciliation as of ToMonthInclusive.
/// </summary>
public sealed class PostgresPayablesReconciliationService(IUnitOfWork uow) : IPayablesReconciliationService
{
    private static readonly Regex SafeTableCode = new("^[a-z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<PayablesReconciliationReport> GetAsync(
        PayablesReconciliationRequest request,
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
        var policy = (context.ApAccountId, context.OpenItemsRegisterId);
        var tableCode = context.TableCode;
        var cursorKind = OpaqueCursorCodec.BuildKind(
            "pm.payables.reconciliation",
            request.FromMonthInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.ToMonthInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ((int)request.Mode).ToString(CultureInfo.InvariantCulture),
            ((int)request.Status).ToString(CultureInfo.InvariantCulture),
            policy.ApAccountId.ToString("N"),
            policy.OpenItemsRegisterId.ToString("N"));
        var pageCursor = string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : OpaqueCursorCodec.Decode<PayablesPageCursor>(cursorKind, request.Cursor);
        var effectiveOffset = pageCursor?.NextOffset ?? requestedOffset;

        var movementsTable = $"opreg_{tableCode}__movements";
        var balancesTable = $"opreg_{tableCode}__balances";
        var movementsTableExists = context.MovementsTableExists;
        var balancesTableExists = context.BalancesTableExists;

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");

        var (glSourceSql, oiSourceSql) = request.Mode switch
        {
            PayablesReconciliationMode.Movement => (
                BuildMovementGlSourceSql(),
                BuildMovementOiSourceSql(movementsTable, movementsTableExists)),
            PayablesReconciliationMode.Balance => (
                BuildBalanceGlSourceSql(),
                BuildBalanceOiSourceSql(movementsTable, movementsTableExists, balancesTable, balancesTableExists)),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };
        var statsSql = pageCursor is null
            ? """
              SELECT
                  COUNT(*)::integer AS total_row_count,
                  COUNT(*) FILTER (WHERE ap_net <> open_items_net)::integer AS total_mismatch_row_count,
                  COUNT(*) FILTER (WHERE ap_net <> 0 AND open_items_net = 0)::integer AS total_gl_only_row_count,
                  COUNT(*) FILTER (WHERE ap_net = 0 AND open_items_net <> 0)::integer AS total_open_items_only_row_count,
                  COALESCE(SUM(ap_net), 0) AS total_ap_net,
                  COALESCE(SUM(open_items_net), 0) AS total_open_items_net
              FROM reconciliation
              """
            : """
              SELECT
                  @KnownRowCount::integer AS total_row_count,
                  @KnownMismatchRowCount::integer AS total_mismatch_row_count,
                  @KnownGlOnlyRowCount::integer AS total_gl_only_row_count,
                  @KnownOpenItemsOnlyRowCount::integer AS total_open_items_only_row_count,
                  @KnownApNet::numeric AS total_ap_net,
                  @KnownOpenItemsNet::numeric AS total_open_items_net
              """;
        var seekPredicateSql = pageCursor is null
            ? string.Empty
            : "WHERE (vendor_id, property_id) > (@AfterVendorId::uuid, @AfterPropertyId::uuid)";
        var offsetSql = pageCursor is null ? "OFFSET @Offset" : string.Empty;

        var sql = $"""
WITH
{glSourceSql},
gl_agg AS (
    SELECT
        COALESCE(p.value_id, @Empty::uuid)   AS vendor_id,
        COALESCE(pr.value_id, @Empty::uuid)  AS property_id,
        SUM(gl_source.net) AS ap_net
    FROM gl_source
    LEFT JOIN platform_dimension_set_items p
        ON p.dimension_set_id = gl_source.dimension_set_id AND p.dimension_id = @PartyDimId::uuid
    LEFT JOIN platform_dimension_set_items pr
        ON pr.dimension_set_id = gl_source.dimension_set_id AND pr.dimension_id = @PropertyDimId::uuid
    GROUP BY 1,2
),
{oiSourceSql},
oi_agg AS (
    SELECT
        COALESCE(p.value_id, @Empty::uuid)   AS vendor_id,
        COALESCE(pr.value_id, @Empty::uuid)  AS property_id,
        SUM(oi_source.net) AS open_items_net
    FROM oi_source
    LEFT JOIN platform_dimension_set_items p
        ON p.dimension_set_id = oi_source.dimension_set_id AND p.dimension_id = @PartyDimId::uuid
    LEFT JOIN platform_dimension_set_items pr
        ON pr.dimension_set_id = oi_source.dimension_set_id AND pr.dimension_id = @PropertyDimId::uuid
    GROUP BY 1,2
),
reconciliation AS (
    SELECT
        COALESCE(gl_agg.vendor_id, oi_agg.vendor_id)      AS vendor_id,
        COALESCE(gl_agg.property_id, oi_agg.property_id)  AS property_id,
        COALESCE(gl_agg.ap_net, 0)                        AS ap_net,
        COALESCE(oi_agg.open_items_net, 0)                AS open_items_net
    FROM gl_agg
    FULL OUTER JOIN oi_agg
        ON gl_agg.vendor_id = oi_agg.vendor_id
       AND gl_agg.property_id = oi_agg.property_id
    WHERE COALESCE(gl_agg.ap_net, 0) <> 0
       OR COALESCE(oi_agg.open_items_net, 0) <> 0
),
filtered_reconciliation AS (
    SELECT *
    FROM reconciliation
    WHERE @Status = 0
       OR (@Status = 1 AND ap_net = open_items_net)
       OR (@Status = 2 AND ap_net <> open_items_net)
       OR (@Status = 3 AND ap_net <> 0 AND open_items_net = 0)
       OR (@Status = 4 AND ap_net = 0 AND open_items_net <> 0)
),
stats AS (
    {statsSql}
),
paged AS (
    SELECT *
    FROM filtered_reconciliation
    {seekPredicateSql}
    ORDER BY vendor_id, property_id
    {offsetSql}
    LIMIT @LimitPlusOne
)
SELECT
    paged.vendor_id AS VendorId,
    paged.property_id AS PropertyId,
    COALESCE(paged.ap_net, 0) AS ApNet,
    COALESCE(paged.open_items_net, 0) AS OpenItemsNet,
    vendor_head.display AS VendorDisplay,
    property_head.display AS PropertyDisplay,
    (paged.vendor_id IS NOT NULL) AS HasRow,
    stats.total_row_count AS TotalRowCount,
    stats.total_mismatch_row_count AS TotalMismatchRowCount,
    stats.total_gl_only_row_count AS TotalGlOnlyRowCount,
    stats.total_open_items_only_row_count AS TotalOpenItemsOnlyRowCount,
    stats.total_ap_net AS TotalApNet,
    stats.total_open_items_net AS TotalOpenItemsNet
FROM stats
LEFT JOIN paged ON TRUE
LEFT JOIN catalogs vendor_catalog
    ON vendor_catalog.id = paged.vendor_id AND vendor_catalog.catalog_code = @PartyCatalogCode
LEFT JOIN cat_pm_party vendor_head ON vendor_head.catalog_id = vendor_catalog.id
LEFT JOIN catalogs property_catalog
    ON property_catalog.id = paged.property_id AND property_catalog.catalog_code = @PropertyCatalogCode
LEFT JOIN cat_pm_property property_head ON property_head.catalog_id = property_catalog.id
ORDER BY paged.vendor_id, paged.property_id;
""";

        var cmd = new CommandDefinition(
            sql,
            new
            {
                policy.ApAccountId,
                FromMonth = request.FromMonthInclusive,
                ToMonth = request.ToMonthInclusive,
                PartyDimId = partyDimId,
                PropertyDimId = propertyDimId,
                PartyCatalogCode = PropertyManagementCodes.Party,
                PropertyCatalogCode = PropertyManagementCodes.Property,
                Offset = requestedOffset,
                Status = (int)request.Status,
                LimitPlusOne = request.Limit + 1,
                AfterVendorId = pageCursor?.AfterVendorId,
                AfterPropertyId = pageCursor?.AfterPropertyId,
                KnownRowCount = pageCursor?.TotalRowCount,
                KnownMismatchRowCount = pageCursor?.TotalMismatchRowCount,
                KnownGlOnlyRowCount = pageCursor?.TotalGlOnlyRowCount,
                KnownOpenItemsOnlyRowCount = pageCursor?.TotalOpenItemsOnlyRowCount,
                KnownApNet = pageCursor?.TotalApNet,
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
                new PayablesPageCursor(
                    pageRows[^1].VendorId,
                    pageRows[^1].PropertyId,
                    effectiveOffset + pageRows.Count,
                    stats.TotalRowCount,
                    stats.TotalMismatchRowCount,
                    stats.TotalGlOnlyRowCount,
                    stats.TotalOpenItemsOnlyRowCount,
                    stats.TotalApNet,
                    stats.TotalOpenItemsNet))
            : null;

        var resultRows = new List<PayablesReconciliationRow>(pageRows.Count);

        foreach (var r in pageRows)
        {
            var diff = r.ApNet - r.OpenItemsNet;
            var hasDiff = diff != 0m;
            var rowKind = ResolveRowKind(r.ApNet, r.OpenItemsNet, hasDiff);

            resultRows.Add(new PayablesReconciliationRow(
                VendorId: r.VendorId,
                VendorDisplay: r.VendorDisplay,
                PropertyId: r.PropertyId,
                PropertyDisplay: r.PropertyDisplay,
                ApNet: r.ApNet,
                OpenItemsNet: r.OpenItemsNet,
                Diff: diff,
                RowKind: rowKind,
                HasDiff: hasDiff));
        }

        return new PayablesReconciliationReport(
            request.FromMonthInclusive,
            request.ToMonthInclusive,
            request.Mode,
            policy.ApAccountId,
            policy.OpenItemsRegisterId,
            TotalApNet: stats.TotalApNet,
            TotalOpenItemsNet: stats.TotalOpenItemsNet,
            TotalDiff: stats.TotalApNet - stats.TotalOpenItemsNet,
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
        SUM(t.credit_amount - t.debit_amount) AS net
    FROM accounting_turnovers t
    WHERE t.account_id = @ApAccountId::uuid
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
    WHERE b.account_id = @ApAccountId::uuid
      AND b.period <= @ToMonth::date
),
gl_seed AS (
    SELECT
        b.dimension_set_id,
        -b.closing_balance AS net
    FROM accounting_balances b
    CROSS JOIN latest_closed lc
    WHERE lc.period IS NOT NULL
      AND b.account_id = @ApAccountId::uuid
      AND b.period = lc.period
),
gl_roll AS (
    SELECT
        t.dimension_set_id,
        SUM(t.credit_amount - t.debit_amount) AS net
    FROM accounting_turnovers t
    CROSS JOIN latest_closed lc
    WHERE t.account_id = @ApAccountId::uuid
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
        Guid VendorId,
        Guid PropertyId,
        decimal ApNet,
        decimal OpenItemsNet,
        string? VendorDisplay,
        string? PropertyDisplay,
        bool HasRow,
        int TotalRowCount,
        int TotalMismatchRowCount,
        int TotalGlOnlyRowCount,
        int TotalOpenItemsOnlyRowCount,
        decimal TotalApNet,
        decimal TotalOpenItemsNet);

    private sealed record PayablesPageCursor(
        Guid AfterVendorId,
        Guid AfterPropertyId,
        int NextOffset,
        int TotalRowCount,
        int TotalMismatchRowCount,
        int TotalGlOnlyRowCount,
        int TotalOpenItemsOnlyRowCount,
        decimal TotalApNet,
        decimal TotalOpenItemsNet);

    internal static PayablesReconciliationRowKind ResolveRowKind(decimal apNet, decimal openItemsNet, bool hasDiff)
    {
        if (apNet != 0m && openItemsNet == 0m)
            return PayablesReconciliationRowKind.GlOnly;

        if (apNet == 0m && openItemsNet != 0m)
            return PayablesReconciliationRowKind.OpenItemsOnly;

        return hasDiff
            ? PayablesReconciliationRowKind.Mismatch
            : PayablesReconciliationRowKind.Matched;
    }

    internal static int ResolveFilteredRowCount(
        PayablesReconciliationStatusFilter status,
        int rowCount,
        int mismatchRowCount,
        int glOnlyRowCount,
        int openItemsOnlyRowCount)
        => status switch
        {
            PayablesReconciliationStatusFilter.All => rowCount,
            PayablesReconciliationStatusFilter.Matched => rowCount - mismatchRowCount,
            PayablesReconciliationStatusFilter.Mismatch => mismatchRowCount,
            PayablesReconciliationStatusFilter.GlOnly => glOnlyRowCount,
            PayablesReconciliationStatusFilter.OpenItemsOnly => openItemsOnlyRowCount,
            _ => throw new NgbArgumentInvalidException(nameof(status), "Select a valid reconciliation status filter.")
        };

    private static void EnsureMonthStart(DateOnly month, string paramName, string label)
    {
        if (month.Day != 1)
            throw new NgbArgumentOutOfRangeException(paramName, month, $"{label} must be the first day of a month.");
    }

    internal static (Guid ApAccountId, Guid OpenItemsRegisterId) EnsureRequiredPolicyValues(
        Guid? apAccountId,
        Guid? openItemsRegisterId)
    {
        var requiredApAccountId = apAccountId.GetValueOrDefault();
        if (requiredApAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                "PM accounting policy has no ap_vendors_account_id configured.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy",
                    ["field"] = "ap_vendors_account_id"
                });
        }

        var requiredOpenItemsRegisterId = openItemsRegisterId.GetValueOrDefault();
        if (requiredOpenItemsRegisterId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                "PM accounting policy has no payables_open_items_register_id configured.",
                new Dictionary<string, object?>
                {
                    ["catalogCode"] = PropertyManagementCodes.AccountingPolicy,
                    ["headTable"] = "cat_pm_accounting_policy",
                    ["field"] = "payables_open_items_register_id"
                });
        }

        return (requiredApAccountId, requiredOpenItemsRegisterId);
    }

    private async Task<QueryContext> ReadQueryContextAsync(CancellationToken ct)
    {
        const string sql = """
WITH policy AS (
    SELECT
        ap_vendors_account_id AS "ApAccountId",
        payables_open_items_register_id AS "OpenItemsRegisterId"
    FROM cat_pm_accounting_policy
    LIMIT 2
)
SELECT
    policy."ApAccountId" AS "ApAccountId",
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
        var (apAccountId, registerId) = EnsureRequiredPolicyValues(row.ApAccountId, row.OpenItemsRegisterId);
        if (!row.ResolvedRegisterId.HasValue)
        {
            throw new NgbConfigurationViolationException(
                "Payables open-items operational register does not exist.",
                new Dictionary<string, object?> { ["registerId"] = registerId });
        }

        return new QueryContext(
            apAccountId,
            registerId,
            EnsureSafeTableCode(row.TableCode, registerId),
            row.MovementsTableExists,
            row.BalancesTableExists);
    }

    private sealed record QueryContext(
        Guid ApAccountId,
        Guid OpenItemsRegisterId,
        string TableCode,
        bool MovementsTableExists,
        bool BalancesTableExists);

    private sealed record QueryContextRow(
        Guid? ApAccountId,
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
                "Payables open-items operational register has empty table_code.",
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
