using System.Text.RegularExpressions;
using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

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

        await uow.EnsureConnectionOpenAsync(ct);

        var policy = await ReadRequiredPolicyAsync(ct);
        var tableCode = await ReadOperationalRegisterTableCodeOrThrowAsync(policy.OpenItemsRegisterId, ct);

        if (!SafeTableCode.IsMatch(tableCode))
        {
            throw new NgbConfigurationViolationException(
                "Operational register table_code is not safe.",
                new Dictionary<string, object?> { ["registerId"] = policy.OpenItemsRegisterId, ["tableCode"] = tableCode });
        }

        var movementsTable = $"opreg_{tableCode}__movements";
        var movementsTableExists = await TableExistsAsync(movementsTable, ct);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");

        var glSourceSql = request.Mode switch
        {
            PayablesReconciliationMode.Movement => BuildMovementGlSourceSql(),
            PayablesReconciliationMode.Balance => BuildBalanceGlSourceSql(),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };

        var oiSourceSql = request.Mode switch
        {
            PayablesReconciliationMode.Movement => BuildMovementOiSourceSql(movementsTable, movementsTableExists),
            PayablesReconciliationMode.Balance => BuildBalanceOiSourceSql(movementsTable, movementsTableExists),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };

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
)
SELECT
    COALESCE(gl_agg.vendor_id, oi_agg.vendor_id)      AS VendorId,
    COALESCE(gl_agg.property_id, oi_agg.property_id)  AS PropertyId,
    COALESCE(gl_agg.ap_net, 0)                        AS ApNet,
    COALESCE(oi_agg.open_items_net, 0)                AS OpenItemsNet
FROM gl_agg
FULL OUTER JOIN oi_agg
    ON gl_agg.vendor_id = oi_agg.vendor_id
   AND gl_agg.property_id = oi_agg.property_id
WHERE COALESCE(gl_agg.ap_net, 0) <> 0
   OR COALESCE(oi_agg.open_items_net, 0) <> 0
ORDER BY 1,2;
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
                Guid.Empty
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<RawRow>(cmd)).AsList();

        var vendorDisplays = await ReadCatalogDisplaysAsync(
            PropertyManagementCodes.Party,
            "cat_pm_party",
            rows.Select(x => x.VendorId),
            ct);

        var propertyDisplays = await ReadCatalogDisplaysAsync(
            PropertyManagementCodes.Property,
            "cat_pm_property",
            rows.Select(x => x.PropertyId),
            ct);

        var resultRows = new List<PayablesReconciliationRow>(rows.Count);
        var totalAp = 0m;
        var totalOi = 0m;
        var mismatchRowCount = 0;

        foreach (var r in rows)
        {
            var diff = r.ApNet - r.OpenItemsNet;
            var hasDiff = diff != 0m;
            var rowKind = ResolveRowKind(r.ApNet, r.OpenItemsNet, hasDiff);

            if (hasDiff)
                mismatchRowCount++;

            resultRows.Add(new PayablesReconciliationRow(
                VendorId: r.VendorId,
                VendorDisplay: ResolveDisplay(vendorDisplays, r.VendorId),
                PropertyId: r.PropertyId,
                PropertyDisplay: ResolveDisplay(propertyDisplays, r.PropertyId),
                ApNet: r.ApNet,
                OpenItemsNet: r.OpenItemsNet,
                Diff: diff,
                RowKind: rowKind,
                HasDiff: hasDiff));

            totalAp += r.ApNet;
            totalOi += r.OpenItemsNet;
        }

        return new PayablesReconciliationReport(
            request.FromMonthInclusive,
            request.ToMonthInclusive,
            request.Mode,
            policy.ApAccountId,
            policy.OpenItemsRegisterId,
            TotalApNet: totalAp,
            TotalOpenItemsNet: totalOi,
            TotalDiff: totalAp - totalOi,
            RowCount: resultRows.Count,
            MismatchRowCount: mismatchRowCount,
            Rows: resultRows);
    }

    private async Task<IReadOnlyDictionary<Guid, string?>> ReadCatalogDisplaysAsync(
        string expectedCatalogCode,
        string typedHeadTable,
        IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var materialized = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (materialized.Length == 0)
            return new Dictionary<Guid, string?>();

        var sql = $"""
SELECT
    c.id      AS Id,
    h.display AS Display
FROM catalogs c
JOIN {typedHeadTable} h
  ON h.catalog_id = c.id
WHERE c.catalog_code = @CatalogCode
  AND c.id = ANY(@Ids);
""";

        var cmd = new CommandDefinition(
            sql,
            new { CatalogCode = expectedCatalogCode, Ids = materialized },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<DisplayRow>(cmd);
        return rows.ToDictionary(x => x.Id, x => x.Display);
    }

    private static string? ResolveDisplay(IReadOnlyDictionary<Guid, string?> displays, Guid id)
    {
        if (id == Guid.Empty)
            return null;

        return displays.GetValueOrDefault(id);
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

    private static string BuildBalanceGlSourceSql() =>
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

    private static string BuildMovementOiSourceSql(string movementsTable, bool movementsTableExists)
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

    private static string BuildBalanceOiSourceSql(string movementsTable, bool movementsTableExists)
        => movementsTableExists
            ? $"""
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

    private static string BuildEmptyOiSourceSql() =>
        """
oi_source AS (
    SELECT
        NULL::uuid AS dimension_set_id,
        NULL::numeric AS net
    WHERE FALSE
)
""";

    private sealed record DisplayRow(Guid Id, string? Display);

    private sealed record RawRow(Guid VendorId, Guid PropertyId, decimal ApNet, decimal OpenItemsNet);

    private static PayablesReconciliationRowKind ResolveRowKind(decimal apNet, decimal openItemsNet, bool hasDiff)
    {
        if (apNet != 0m && openItemsNet == 0m)
            return PayablesReconciliationRowKind.GlOnly;

        if (apNet == 0m && openItemsNet != 0m)
            return PayablesReconciliationRowKind.OpenItemsOnly;

        return hasDiff
            ? PayablesReconciliationRowKind.Mismatch
            : PayablesReconciliationRowKind.Matched;
    }

    private static void EnsureMonthStart(DateOnly month, string paramName, string label)
    {
        if (month.Day != 1)
            throw new NgbArgumentOutOfRangeException(paramName, month, $"{label} must be the first day of a month.");
    }

    private async Task<(Guid ApAccountId, Guid OpenItemsRegisterId)> ReadRequiredPolicyAsync(CancellationToken ct)
    {
        const string sql = """
SELECT
    ap_vendors_account_id AS ApAccountId,
    payables_open_items_register_id AS OpenItemsRegisterId
FROM cat_pm_accounting_policy
LIMIT 2;
""";

        var rows = (await uow.Connection.QueryAsync<PolicyRow>(
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

        var r = rows[0];

        if (r.ApAccountId is null || r.ApAccountId == Guid.Empty)
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

        if (r.OpenItemsRegisterId is null || r.OpenItemsRegisterId == Guid.Empty)
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

        return (r.ApAccountId.Value, r.OpenItemsRegisterId.Value);
    }

    private sealed record PolicyRow(Guid? ApAccountId, Guid? OpenItemsRegisterId);

    private async Task<string> ReadOperationalRegisterTableCodeOrThrowAsync(Guid registerId, CancellationToken ct)
    {
        const string sql = """
SELECT table_code AS TableCode
FROM operational_registers
WHERE register_id = @RegisterId::uuid
LIMIT 2;
""";

        var rows = (await uow.Connection.QueryAsync<TableCodeRow>(
            new CommandDefinition(sql, new { RegisterId = registerId }, transaction: uow.Transaction, cancellationToken: ct))).AsList();

        if (rows.Count == 0)
            throw new NgbConfigurationViolationException(
                "Payables open-items operational register does not exist.",
                new Dictionary<string, object?> { ["registerId"] = registerId });

        if (rows.Count > 1)
            throw new NgbConfigurationViolationException(
                "Multiple operational register rows found for a single register_id.",
                new Dictionary<string, object?> { ["registerId"] = registerId });

        var tableCode = rows[0].TableCode?.Trim();
        if (string.IsNullOrWhiteSpace(tableCode))
        {
            throw new NgbConfigurationViolationException(
                "Payables open-items operational register has empty table_code.",
                new Dictionary<string, object?> { ["registerId"] = registerId });
        }

        return tableCode;
    }

    private sealed record TableCodeRow(string? TableCode);

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        const string sql = """
SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_name = @TableName
);
""";

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { TableName = tableName }, transaction: uow.Transaction, cancellationToken: ct));
    }
}
