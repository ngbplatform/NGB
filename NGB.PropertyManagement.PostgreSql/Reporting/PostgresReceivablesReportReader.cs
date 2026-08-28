using Dapper;
using NGB.Contracts.Common;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresReceivablesReportReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources)
    : IReceivablesReportReader
{
    private static readonly Guid LeaseDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

    public async Task<ReceivablesReportPage> GetPageAsync(
        Guid registerId,
        Guid leaseId,
        ReceivablesReportMode mode,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        leaseId.EnsureNonEmpty(nameof(leaseId));

        if (!Enum.IsDefined(mode))
            throw new NgbArgumentOutOfRangeException(nameof(mode), mode, "Unknown receivables report mode.");

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        await uow.EnsureConnectionOpenAsync(ct);

        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);

        var resourceColumns = (await resources.GetByRegisterIdAsync(registerId, ct))
            .Select(static resource => resource.ColumnCode)
            .ToHashSet(StringComparer.Ordinal);

        if (!resourceColumns.Contains("amount"))
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column 'amount'.");

        var tableName = OperationalRegisterNaming.MovementsTable(register.TableCode);
        var balancesTableName = OperationalRegisterNaming.BalancesTable(register.TableCode);
        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = tableName },
            uow.Transaction,
            cancellationToken: ct));

        if (!exists)
            return new ReceivablesReportPage([], 0, 0m, 0m, 0m, null, null, null);

        var balancesExist = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = balancesTableName },
            uow.Transaction,
            cancellationToken: ct));

        var chargesOnly = mode == ReceivablesReportMode.Aging;
        var orderBy = chargesOnly
            ? "item.due_on_utc, item.document_id"
            : "CASE WHEN item.net_amount > 0 THEN 0 ELSE 1 END, COALESCE(item.due_on_utc, item.received_on_utc), item.document_id";
        var netSourceSql = balancesExist
            ? BuildSnapshotBackedNetSourceSql(tableName, balancesTableName)
            : BuildMovementOnlyNetSourceSql(tableName);
        var sql = $"""
{netSourceSql},
nets AS (
    SELECT
        item.value_id AS document_id,
        SUM(source.net_amount) AS net_amount
    FROM dimension_nets source
    JOIN platform_dimension_set_items item
      ON item.dimension_set_id = source.dimension_set_id
     AND item.dimension_id = @ItemDimensionId
    GROUP BY item.value_id
    HAVING SUM(source.net_amount) <> 0
),
items AS (
    SELECT
        nets.document_id,
        document.type_code AS document_type,
        COALESCE(charge.display, late_fee.display, rent.display, payment.display, credit_memo.display, document.number) AS display,
        COALESCE(charge.due_on_utc, late_fee.due_on_utc, rent.due_on_utc) AS due_on_utc,
        COALESCE(payment.received_on_utc, credit_memo.credited_on_utc) AS received_on_utc,
        CASE
            WHEN charge.document_id IS NOT NULL THEN charge_type.display
            WHEN late_fee.document_id IS NOT NULL THEN 'Late Fee'
            WHEN rent.document_id IS NOT NULL THEN 'Rent'
            ELSE NULL
        END AS charge_type_display,
        COALESCE(charge.amount, late_fee.amount, rent.amount, payment.amount, credit_memo.amount, 0) AS original_amount,
        nets.net_amount
    FROM nets
    JOIN documents document ON document.id = nets.document_id
    LEFT JOIN doc_pm_receivable_charge charge ON charge.document_id = nets.document_id
    LEFT JOIN cat_pm_receivable_charge_type charge_type ON charge_type.catalog_id = charge.charge_type_id
    LEFT JOIN doc_pm_late_fee_charge late_fee ON late_fee.document_id = nets.document_id
    LEFT JOIN doc_pm_rent_charge rent ON rent.document_id = nets.document_id
    LEFT JOIN doc_pm_receivable_payment payment ON payment.document_id = nets.document_id
    LEFT JOIN doc_pm_receivable_credit_memo credit_memo ON credit_memo.document_id = nets.document_id
    WHERE (@ChargesOnly = FALSE OR nets.net_amount > 0)
      AND (
          charge.document_id IS NOT NULL
          OR late_fee.document_id IS NOT NULL
          OR rent.document_id IS NOT NULL
          OR payment.document_id IS NOT NULL
          OR credit_memo.document_id IS NOT NULL
      )
),
stats AS (
    SELECT
        COUNT(*)::integer AS total_count,
        COALESCE(SUM(CASE WHEN net_amount > 0 THEN original_amount ELSE 0 END), 0) AS total_original,
        COALESCE(SUM(CASE WHEN net_amount > 0 THEN net_amount ELSE 0 END), 0) AS total_outstanding,
        COALESCE(SUM(CASE WHEN net_amount < 0 THEN -net_amount ELSE 0 END), 0) AS total_credit
    FROM items
),
lease_context AS (
    SELECT
        party.display AS party_display,
        property.display AS property_display,
        lease.display AS lease_display
    FROM doc_pm_lease lease
    LEFT JOIN doc_pm_lease__parties lease_party
      ON lease_party.document_id = lease.document_id
     AND lease_party.is_primary = TRUE
    LEFT JOIN cat_pm_party party ON party.catalog_id = lease_party.party_id
    LEFT JOIN cat_pm_property property ON property.catalog_id = lease.property_id
    WHERE lease.document_id = @LeaseId
),
paged AS (
    SELECT item.*
    FROM items item
    ORDER BY {orderBy}
    OFFSET @Offset
    LIMIT @Limit
)
SELECT
    paged.document_id AS DocumentId,
    paged.document_type AS DocumentType,
    paged.display AS Display,
    paged.due_on_utc AS DueOnUtc,
    paged.received_on_utc AS ReceivedOnUtc,
    paged.charge_type_display AS ChargeTypeDisplay,
    paged.original_amount AS OriginalAmount,
    paged.net_amount AS NetAmount,
    (paged.document_id IS NOT NULL) AS HasRow,
    stats.total_count AS TotalCount,
    stats.total_original AS TotalOriginal,
    stats.total_outstanding AS TotalOutstanding,
    stats.total_credit AS TotalCredit,
    context.party_display AS PartyDisplay,
    context.property_display AS PropertyDisplay,
    context.lease_display AS LeaseDisplay
FROM stats
LEFT JOIN lease_context context ON TRUE
LEFT JOIN paged ON TRUE
ORDER BY {orderBy.Replace("item.", "paged.")};
""";

        var rows = (await uow.Connection.QueryAsync<ReceivablesReportSqlRow>(new CommandDefinition(
            sql,
            new
            {
                LeaseDimensionId,
                ItemDimensionId,
                LeaseId = leaseId,
                ChargesOnly = chargesOnly,
                Offset = PagingLimits.BoundOffset(offset),
                Limit = limit
            },
            uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();
        if (first is null)
            return new ReceivablesReportPage([], 0, 0m, 0m, 0m, null, null, null);

        return new ReceivablesReportPage(
            rows.Where(static row => row.HasRow).Select(static row => new ReceivablesReportRow(
                IsCharge: row.NetAmount > 0m,
                DocumentId: row.DocumentId!.Value,
                DocumentType: row.DocumentType!,
                Display: row.Display,
                DueOnUtc: row.DueOnUtc,
                ReceivedOnUtc: row.ReceivedOnUtc,
                ChargeTypeDisplay: row.ChargeTypeDisplay,
                OriginalAmount: row.OriginalAmount,
                OpenAmount: Math.Abs(row.NetAmount))).ToArray(),
            first.TotalCount,
            first.TotalOriginal,
            first.TotalOutstanding,
            first.TotalCredit,
            first.PartyDisplay,
            first.PropertyDisplay,
            first.LeaseDisplay);
    }

    private static string BuildMovementOnlyNetSourceSql(string movementsTable) => $"""
WITH lease_dimension_sets AS (
    SELECT dimension_set_id
    FROM platform_dimension_set_items
    WHERE dimension_id = @LeaseDimensionId
      AND value_id = @LeaseId
),
dimension_nets AS (
    SELECT
        movement.dimension_set_id,
        SUM(CASE WHEN movement.is_storno THEN -movement.amount ELSE movement.amount END) AS net_amount
    FROM {movementsTable} movement
    JOIN lease_dimension_sets lease
      ON lease.dimension_set_id = movement.dimension_set_id
    GROUP BY movement.dimension_set_id
)
""";

    private static string BuildSnapshotBackedNetSourceSql(string movementsTable, string balancesTable) => $"""
WITH lease_dimension_sets AS (
    SELECT dimension_set_id
    FROM platform_dimension_set_items
    WHERE dimension_id = @LeaseDimensionId
      AND value_id = @LeaseId
),
latest_snapshot AS (
    SELECT MAX(period_month) AS period_month
    FROM {balancesTable}
),
snapshot_values AS (
    SELECT balance.dimension_set_id, balance.amount AS net_amount
    FROM {balancesTable} balance
    JOIN lease_dimension_sets lease
      ON lease.dimension_set_id = balance.dimension_set_id
    CROSS JOIN latest_snapshot latest
    WHERE balance.period_month = latest.period_month
),
movement_values AS (
    SELECT
        movement.dimension_set_id,
        SUM(CASE WHEN movement.is_storno THEN -movement.amount ELSE movement.amount END) AS net_amount
    FROM {movementsTable} movement
    JOIN lease_dimension_sets lease
      ON lease.dimension_set_id = movement.dimension_set_id
    CROSS JOIN latest_snapshot latest
    WHERE latest.period_month IS NULL OR movement.period_month > latest.period_month
    GROUP BY movement.dimension_set_id
),
dimension_nets AS (
    SELECT
        keys.dimension_set_id,
        COALESCE(snapshot.net_amount, 0) + COALESCE(delta.net_amount, 0) AS net_amount
    FROM (
        SELECT dimension_set_id FROM snapshot_values
        UNION
        SELECT dimension_set_id FROM movement_values
    ) keys
    LEFT JOIN snapshot_values snapshot ON snapshot.dimension_set_id = keys.dimension_set_id
    LEFT JOIN movement_values delta ON delta.dimension_set_id = keys.dimension_set_id
)
""";

    private sealed record ReceivablesReportSqlRow(
        Guid? DocumentId,
        string? DocumentType,
        string? Display,
        DateOnly? DueOnUtc,
        DateOnly? ReceivedOnUtc,
        string? ChargeTypeDisplay,
        decimal OriginalAmount,
        decimal NetAmount,
        bool HasRow,
        int TotalCount,
        decimal TotalOriginal,
        decimal TotalOutstanding,
        decimal TotalCredit,
        string? PartyDisplay,
        string? PropertyDisplay,
        string? LeaseDisplay);
}
