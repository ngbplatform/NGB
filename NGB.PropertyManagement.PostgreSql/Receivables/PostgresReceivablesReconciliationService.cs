using System.Text.RegularExpressions;
using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

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
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var glSourceSql = request.Mode switch
        {
            ReceivablesReconciliationMode.Movement => BuildMovementGlSourceSql(),
            ReceivablesReconciliationMode.Balance => BuildBalanceGlSourceSql(),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };

        var oiSourceSql = request.Mode switch
        {
            ReceivablesReconciliationMode.Movement => BuildMovementOiSourceSql(movementsTable, movementsTableExists),
            ReceivablesReconciliationMode.Balance => BuildBalanceOiSourceSql(movementsTable, movementsTableExists),
            _ => throw new NgbArgumentInvalidException(nameof(request.Mode), "Select a valid reconciliation mode.")
        };

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
)
SELECT
    COALESCE(gl_agg.party_id, oi_agg.party_id)        AS PartyId,
    COALESCE(gl_agg.property_id, oi_agg.property_id)  AS PropertyId,
    COALESCE(gl_agg.lease_id, oi_agg.lease_id)        AS LeaseId,
    COALESCE(gl_agg.ar_net, 0)                        AS ArNet,
    COALESCE(oi_agg.open_items_net, 0)                AS OpenItemsNet
FROM gl_agg
FULL OUTER JOIN oi_agg
    ON gl_agg.party_id = oi_agg.party_id
   AND gl_agg.property_id = oi_agg.property_id
   AND gl_agg.lease_id = oi_agg.lease_id
WHERE COALESCE(gl_agg.ar_net, 0) <> 0
   OR COALESCE(oi_agg.open_items_net, 0) <> 0
ORDER BY 1,2,3;
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
                Guid.Empty
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<RawRow>(cmd)).AsList();

        var partyDisplays = await ReadCatalogDisplaysAsync(PropertyManagementCodes.Party, "cat_pm_party", rows.Select(x => x.PartyId), ct);
        var propertyDisplays = await ReadCatalogDisplaysAsync(PropertyManagementCodes.Property, "cat_pm_property", rows.Select(x => x.PropertyId), ct);
        var leaseDisplays = await ReadDocumentDisplaysAsync(PropertyManagementCodes.Lease, "doc_pm_lease", rows.Select(x => x.LeaseId), ct);

        var resultRows = new List<ReceivablesReconciliationRow>(rows.Count);
        decimal totalAr = 0m;
        decimal totalOi = 0m;
        var mismatchRowCount = 0;

        foreach (var r in rows)
        {
            var diff = r.ArNet - r.OpenItemsNet;
            var hasDiff = diff != 0m;
            var rowKind = ResolveRowKind(r.ArNet, r.OpenItemsNet, hasDiff);
            if (hasDiff)
                mismatchRowCount++;

            resultRows.Add(new ReceivablesReconciliationRow(
                PartyId: r.PartyId,
                PartyDisplay: ResolveDisplay(partyDisplays, r.PartyId),
                PropertyId: r.PropertyId,
                PropertyDisplay: ResolveDisplay(propertyDisplays, r.PropertyId),
                LeaseId: r.LeaseId,
                LeaseDisplay: ResolveDisplay(leaseDisplays, r.LeaseId),
                ArNet: r.ArNet,
                OpenItemsNet: r.OpenItemsNet,
                Diff: diff,
                RowKind: rowKind,
                HasDiff: hasDiff));

            totalAr += r.ArNet;
            totalOi += r.OpenItemsNet;
        }

        return new ReceivablesReconciliationReport(
            request.FromMonthInclusive,
            request.ToMonthInclusive,
            request.Mode,
            policy.ArAccountId,
            policy.OpenItemsRegisterId,
            TotalArNet: totalAr,
            TotalOpenItemsNet: totalOi,
            TotalDiff: totalAr - totalOi,
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

    private async Task<IReadOnlyDictionary<Guid, string?>> ReadDocumentDisplaysAsync(
        string expectedTypeCode,
        string typedHeadTable,
        IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var materialized = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (materialized.Length == 0)
            return new Dictionary<Guid, string?>();

        var sql = $"""
SELECT
    d.id      AS Id,
    h.display AS Display
FROM documents d
JOIN {typedHeadTable} h
  ON h.document_id = d.id
WHERE d.type_code = @TypeCode
  AND d.id = ANY(@Ids);
""";

        var cmd = new CommandDefinition(
            sql,
            new { TypeCode = expectedTypeCode, Ids = materialized },
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
        SUM(t.debit_amount - t.credit_amount) AS net
    FROM accounting_turnovers t
    WHERE t.account_id = @ArAccountId::uuid
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

    private sealed record RawRow(Guid PartyId, Guid PropertyId, Guid LeaseId, decimal ArNet, decimal OpenItemsNet);

    private static ReceivablesReconciliationRowKind ResolveRowKind(decimal arNet, decimal openItemsNet, bool hasDiff)
    {
        if (arNet != 0m && openItemsNet == 0m)
            return ReceivablesReconciliationRowKind.GlOnly;

        if (arNet == 0m && openItemsNet != 0m)
            return ReceivablesReconciliationRowKind.OpenItemsOnly;

        return hasDiff
            ? ReceivablesReconciliationRowKind.Mismatch
            : ReceivablesReconciliationRowKind.Matched;
    }

    private static void EnsureMonthStart(DateOnly month, string paramName, string label)
    {
        if (month.Day != 1)
            throw new NgbArgumentOutOfRangeException(paramName, month, $"{label} must be the first day of a month.");
    }

    private async Task<(Guid ArAccountId, Guid OpenItemsRegisterId)> ReadRequiredPolicyAsync(CancellationToken ct)
    {
        const string sql = """
SELECT
    ar_tenants_account_id AS ArAccountId,
    receivables_open_items_register_id AS OpenItemsRegisterId
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

        if (r.ArAccountId is null || r.ArAccountId == Guid.Empty)
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

        if (r.OpenItemsRegisterId is null || r.OpenItemsRegisterId == Guid.Empty)
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

        return (r.ArAccountId.Value, r.OpenItemsRegisterId.Value);
    }

    private sealed record PolicyRow(Guid? ArAccountId, Guid? OpenItemsRegisterId);

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
                "Receivables open-items operational register does not exist.",
                new Dictionary<string, object?> { ["registerId"] = registerId });

        if (rows.Count > 1)
            throw new NgbConfigurationViolationException(
                "Multiple operational register rows found for a single register_id.",
                new Dictionary<string, object?> { ["registerId"] = registerId });

        var tableCode = rows[0].TableCode?.Trim();
        if (string.IsNullOrWhiteSpace(tableCode))
        {
            throw new NgbConfigurationViolationException(
                "Operational register has no table_code.",
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

        return await uow.Connection.QuerySingleAsync<bool>(
            new CommandDefinition(sql, new { TableName = tableName }, transaction: uow.Transaction, cancellationToken: ct));
    }
}
