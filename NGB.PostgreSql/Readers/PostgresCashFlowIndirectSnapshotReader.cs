using Dapper;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresCashFlowIndirectSnapshotReader(IUnitOfWork uow)
    : ICashFlowIndirectSnapshotReader
{
    private static readonly short[] BalanceRoles =
    [
        (short)CashFlowRole.CashEquivalent,
        (short)CashFlowRole.WorkingCapital
    ];

    private static readonly short[] ProfitAndLossSections =
    [
        (short)StatementSection.Income,
        (short)StatementSection.CostOfGoodsSold,
        (short)StatementSection.Expenses,
        (short)StatementSection.OtherIncome,
        (short)StatementSection.OtherExpense
    ];

    private static readonly short[] CounterpartyRoles =
    [
        (short)CashFlowRole.InvestingCounterparty,
        (short)CashFlowRole.FinancingCounterparty
    ];

    private static readonly short[] NonPnlSections =
    [
        (short)StatementSection.Assets,
        (short)StatementSection.Liabilities,
        (short)StatementSection.Equity
    ];

    public async Task<CashFlowIndirectSnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        var beginningAsOfDate = fromInclusive.AddDays(-1);
        var latestClosed = await GetLatestClosedPeriodsAsync(beginningAsOfDate, toInclusive, ct);
        var lineDefinitions = await LoadLineDefinitionsAsync(ct);

        var openingRows = await LoadBalanceStateRowsAsync(beginningAsOfDate, latestClosed.BeginningLatestClosedPeriod, ct);
        var closingRows = await LoadBalanceStateRowsAsync(toInclusive, latestClosed.EndingLatestClosedPeriod, ct);

        var combined = new Dictionary<Guid, MutableBalanceRow>();
        foreach (var row in openingRows)
        {
            if (!combined.TryGetValue(row.AccountId, out var current))
            {
                current = new MutableBalanceRow(row.AccountId, row.AccountCode, row.AccountName, row.StatementSection, row.CashFlowRole, row.CashFlowLineCode);
                combined[row.AccountId] = current;
            }

            current.OpeningBalance += row.ClosingBalance;
        }

        foreach (var row in closingRows)
        {
            if (!combined.TryGetValue(row.AccountId, out var current))
            {
                current = new MutableBalanceRow(row.AccountId, row.AccountCode, row.AccountName, row.StatementSection, row.CashFlowRole, row.CashFlowLineCode);
                combined[row.AccountId] = current;
            }

            current.ClosingBalance += row.ClosingBalance;
        }

        var beginningCash = 0m;
        var endingCash = 0m;
        var workingCapitalLines = new Dictionary<string, MutableLine>();

        foreach (var row in combined.Values)
        {
            switch (row.CashFlowRole)
            {
                case CashFlowRole.CashEquivalent:
                    beginningCash += row.OpeningBalance;
                    endingCash += row.ClosingBalance;
                    break;

                case CashFlowRole.WorkingCapital:
                {
                    var line = ResolveLine(lineDefinitions, row.CashFlowLineCode, row.AccountCode);
                    AddAmount(workingCapitalLines, line, -(row.ClosingBalance - row.OpeningBalance));
                    break;
                }
            }
        }

        var pnlRows = await LoadProfitAndLossRangeRowsAsync(fromInclusive, toInclusive, ct);
        var netIncome = pnlRows.Sum(x => x.NetMovement);

        var operatingLines = new Dictionary<string, MutableLine>();
        foreach (var wc in workingCapitalLines.Values)
        {
            AddAmount(operatingLines, wc, wc.Amount);
        }

        foreach (var row in pnlRows)
        {
            if (row.CashFlowRole != CashFlowRole.NonCashOperatingAdjustment)
                continue;

            var line = ResolveLine(lineDefinitions, row.CashFlowLineCode, row.AccountCode);
            AddAmount(operatingLines, line, -row.NetMovement);
        }

        var cashMovementRows = await LoadCashMovementRowsAsync(fromInclusive, toInclusive, ct);
        var investingLines = cashMovementRows
            .Where(x => x.Section == CashFlowSection.Investing)
            .ToArray();
        var financingLines = cashMovementRows
            .Where(x => x.Section == CashFlowSection.Financing)
            .ToArray();

        var unclassifiedCashRows = await LoadUnclassifiedCashRowsAsync(fromInclusive, toInclusive, ct);

        return new CashFlowIndirectSnapshot(
            NetIncome: netIncome,
            OperatingLines: operatingLines.Values
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .Select(x => new CashFlowIndirectSnapshotLine(CashFlowSection.Operating, x.LineCode, x.Label, x.SortOrder, x.Amount))
                .ToArray(),
            InvestingLines: investingLines,
            FinancingLines: financingLines,
            BeginningCash: beginningCash,
            EndingCash: endingCash,
            BeginningLatestClosedPeriod: latestClosed.BeginningLatestClosedPeriod,
            BeginningRollForwardPeriods: latestClosed.BeginningLatestClosedPeriod is null
                ? 0
                : CountPeriods(latestClosed.BeginningLatestClosedPeriod.Value.AddMonths(1), StartOfMonth(beginningAsOfDate)),
            EndingLatestClosedPeriod: latestClosed.EndingLatestClosedPeriod,
            EndingRollForwardPeriods: latestClosed.EndingLatestClosedPeriod is null
                ? 0
                : CountPeriods(latestClosed.EndingLatestClosedPeriod.Value.AddMonths(1), StartOfMonth(toInclusive)),
            UnclassifiedCashRows: unclassifiedCashRows);
    }

    private async Task<LatestClosedPeriodsRow> GetLatestClosedPeriodsAsync(
        DateOnly beginningAsOfDate,
        DateOnly endingAsOfDate,
        CancellationToken ct)
    {
        const string sql = """
                           SELECT
                               MAX(period) FILTER (
                                   WHERE period < @BeginningMonthStart::date
                                      OR (period = @BeginningMonthStart::date AND @BeginningUseCurrentMonthSnapshot::boolean = TRUE)
                               ) AS BeginningLatestClosedPeriod,
                               MAX(period) FILTER (
                                   WHERE period < @EndingMonthStart::date
                                      OR (period = @EndingMonthStart::date AND @EndingUseCurrentMonthSnapshot::boolean = TRUE)
                               ) AS EndingLatestClosedPeriod
                           FROM accounting_closed_periods;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QuerySingleAsync<LatestClosedPeriodsRow>(
            new CommandDefinition(
                sql,
                new
                {
                    BeginningMonthStart = StartOfMonth(beginningAsOfDate),
                    EndingMonthStart = StartOfMonth(endingAsOfDate),
                    BeginningUseCurrentMonthSnapshot = beginningAsOfDate == EndOfMonth(beginningAsOfDate),
                    EndingUseCurrentMonthSnapshot = endingAsOfDate == EndOfMonth(endingAsOfDate)
                },
                transaction: uow.Transaction,
                cancellationToken: ct)))!;
    }

    private Task<IReadOnlyList<BalanceStateRow>> LoadBalanceStateRowsAsync(
        DateOnly asOfDate,
        DateOnly? latestClosedSnapshotPeriod,
        CancellationToken ct)
        => latestClosedSnapshotPeriod switch
        {
            null => LoadBalanceStateInceptionToDateRowsAsync(asOfDate, ct),
            { } snapshotPeriod when snapshotPeriod == StartOfMonth(asOfDate) && asOfDate == EndOfMonth(asOfDate)
                => LoadBalanceStateSnapshotOnlyRowsAsync(snapshotPeriod, ct),
            { } snapshotPeriod => LoadBalanceStateSnapshotPlusDeltaRowsAsync(snapshotPeriod, asOfDate, ct)
        };

    private async Task<IReadOnlyList<BalanceStateRow>> LoadBalanceStateInceptionToDateRowsAsync(
        DateOnly asOfDate,
        CancellationToken ct)
    {
        var sql = """
                  WITH ledger_rows AS (
                      SELECT
                          r.debit_account_id AS AccountId,
                          SUM(r.amount) AS ClosingBalance
                      FROM accounting_register_main r
                      WHERE r.period < @ToExclusiveUtc
                      GROUP BY r.debit_account_id

                      UNION ALL

                      SELECT
                          r.credit_account_id AS AccountId,
                          SUM(-r.amount) AS ClosingBalance
                      FROM accounting_register_main r
                      WHERE r.period < @ToExclusiveUtc
                      GROUP BY r.credit_account_id
                  ),
                  final_rows AS (
                      SELECT
                          AccountId,
                          SUM(ClosingBalance) AS ClosingBalance
                      FROM ledger_rows
                      GROUP BY AccountId
                  )
                  SELECT
                      fr.AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      a.cash_flow_role AS CashFlowRole,
                      a.cash_flow_line_code AS CashFlowLineCode,
                      fr.ClosingBalance AS ClosingBalance
                  FROM final_rows fr
                  JOIN accounting_accounts a
                    ON a.account_id = fr.AccountId
                   AND a.is_deleted = FALSE
                  WHERE a.cash_flow_role = ANY(@RelevantRoles::smallint[])
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync<BalanceStateRow>(
            sql,
            new
            {
                ToExclusiveUtc = ToExclusiveUtc(asOfDate),
                RelevantRoles = BalanceRoles
            },
            ct);
    }

    private async Task<IReadOnlyList<BalanceStateRow>> LoadBalanceStateSnapshotOnlyRowsAsync(
        DateOnly snapshotPeriod,
        CancellationToken ct)
    {
        var sql = """
                  SELECT
                      b.account_id AS AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      a.cash_flow_role AS CashFlowRole,
                      a.cash_flow_line_code AS CashFlowLineCode,
                      SUM(b.closing_balance) AS ClosingBalance
                  FROM accounting_balances b
                  JOIN accounting_accounts a
                    ON a.account_id = b.account_id
                   AND a.is_deleted = FALSE
                  WHERE b.period = @SnapshotPeriod::date
                    AND a.cash_flow_role = ANY(@RelevantRoles::smallint[])
                  GROUP BY b.account_id, a.code, a.name, a.statement_section, a.cash_flow_role, a.cash_flow_line_code
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync<BalanceStateRow>(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                RelevantRoles = BalanceRoles
            },
            ct);
    }

    private async Task<IReadOnlyList<BalanceStateRow>> LoadBalanceStateSnapshotPlusDeltaRowsAsync(
        DateOnly snapshotPeriod,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        var sql = """
                  WITH snapshot_rows AS (
                      SELECT
                          b.account_id AS AccountId,
                          SUM(b.closing_balance) AS ClosingBalance
                      FROM accounting_balances b
                      JOIN accounting_accounts a
                        ON a.account_id = b.account_id
                       AND a.is_deleted = FALSE
                      WHERE b.period = @SnapshotPeriod::date
                        AND a.cash_flow_role = ANY(@RelevantRoles::smallint[])
                      GROUP BY b.account_id
                  ),
                  delta_rows AS (
                      SELECT
                          combined.AccountId,
                          SUM(combined.ClosingBalance) AS ClosingBalance
                      FROM (
                          SELECT
                              r.debit_account_id AS AccountId,
                              SUM(r.amount) AS ClosingBalance
                          FROM accounting_register_main r
                          WHERE r.period >= @DeltaFromUtc
                            AND r.period < @ToExclusiveUtc
                          GROUP BY r.debit_account_id

                          UNION ALL

                          SELECT
                              r.credit_account_id AS AccountId,
                              SUM(-r.amount) AS ClosingBalance
                          FROM accounting_register_main r
                          WHERE r.period >= @DeltaFromUtc
                            AND r.period < @ToExclusiveUtc
                          GROUP BY r.credit_account_id
                      ) combined
                      GROUP BY combined.AccountId
                  ),
                  final_rows AS (
                      SELECT
                          combined.AccountId,
                          SUM(combined.ClosingBalance) AS ClosingBalance
                      FROM (
                          SELECT AccountId, ClosingBalance FROM snapshot_rows
                          UNION ALL
                          SELECT AccountId, ClosingBalance FROM delta_rows
                      ) combined
                      GROUP BY combined.AccountId
                  )
                  SELECT
                      fr.AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      a.cash_flow_role AS CashFlowRole,
                      a.cash_flow_line_code AS CashFlowLineCode,
                      fr.ClosingBalance AS ClosingBalance
                  FROM final_rows fr
                  JOIN accounting_accounts a
                    ON a.account_id = fr.AccountId
                   AND a.is_deleted = FALSE
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync<BalanceStateRow>(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                DeltaFromUtc = StartOfNextMonthUtc(snapshotPeriod),
                ToExclusiveUtc = ToExclusiveUtc(asOfDate),
                RelevantRoles = BalanceRoles
            },
            ct);
    }

    private async Task<IReadOnlyList<ProfitAndLossRangeRow>> LoadProfitAndLossRangeRowsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct)
    {
        var sql = """
                  WITH range_rows AS (
                      SELECT
                          r.debit_account_id AS AccountId,
                          SUM(r.amount) AS DebitAmount,
                          0::numeric AS CreditAmount
                      FROM accounting_register_main r
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                      GROUP BY r.debit_account_id

                      UNION ALL

                      SELECT
                          r.credit_account_id AS AccountId,
                          0::numeric AS DebitAmount,
                          SUM(r.amount) AS CreditAmount
                      FROM accounting_register_main r
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                      GROUP BY r.credit_account_id
                  ),
                  final_rows AS (
                      SELECT
                          AccountId,
                          SUM(DebitAmount) AS DebitAmount,
                          SUM(CreditAmount) AS CreditAmount
                      FROM range_rows
                      GROUP BY AccountId
                  )
                  SELECT
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      a.cash_flow_role AS CashFlowRole,
                      a.cash_flow_line_code AS CashFlowLineCode,
                      (fr.CreditAmount - fr.DebitAmount) AS NetMovement
                  FROM final_rows fr
                  JOIN accounting_accounts a
                    ON a.account_id = fr.AccountId
                   AND a.is_deleted = FALSE
                  WHERE a.statement_section = ANY(@ProfitAndLossSections::smallint[])
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync<ProfitAndLossRangeRow>(
            sql,
            new
            {
                FromUtc = StartOfDayUtc(fromInclusive),
                ToExclusiveUtc = ToExclusiveUtc(toInclusive),
                ProfitAndLossSections
            },
            ct);
    }

    private async Task<IReadOnlyList<CashFlowIndirectSnapshotLine>> LoadCashMovementRowsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct)
    {
        var fromUtc = StartOfDayUtc(fromInclusive);
        var toExclusiveUtc = ToExclusiveUtc(toInclusive);

        var sql = """
                  WITH cash_movement_rows AS (
                      SELECT
                          l.section AS Section,
                          l.line_code AS LineCode,
                          l.label AS Label,
                          l.sort_order AS SortOrder,
                          SUM(r.amount) AS Amount
                      FROM accounting_register_main r
                      JOIN accounting_accounts cash
                        ON cash.account_id = r.debit_account_id
                       AND cash.is_deleted = FALSE
                       AND cash.cash_flow_role = @CashEquivalentRole
                      JOIN accounting_accounts counter
                        ON counter.account_id = r.credit_account_id
                       AND counter.is_deleted = FALSE
                      JOIN accounting_cash_flow_lines l
                        ON l.line_code = counter.cash_flow_line_code
                       AND l.method = @IndirectMethod
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                        AND counter.cash_flow_role = ANY(@CounterpartyRoles::smallint[])
                      GROUP BY l.section, l.line_code, l.label, l.sort_order

                      UNION ALL

                      SELECT
                          l.section AS Section,
                          l.line_code AS LineCode,
                          l.label AS Label,
                          l.sort_order AS SortOrder,
                          SUM(-r.amount) AS Amount
                      FROM accounting_register_main r
                      JOIN accounting_accounts cash
                        ON cash.account_id = r.credit_account_id
                       AND cash.is_deleted = FALSE
                       AND cash.cash_flow_role = @CashEquivalentRole
                      JOIN accounting_accounts counter
                        ON counter.account_id = r.debit_account_id
                       AND counter.is_deleted = FALSE
                      JOIN accounting_cash_flow_lines l
                        ON l.line_code = counter.cash_flow_line_code
                       AND l.method = @IndirectMethod
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                        AND counter.cash_flow_role = ANY(@CounterpartyRoles::smallint[])
                      GROUP BY l.section, l.line_code, l.label, l.sort_order
                  )
                  SELECT
                      Section,
                      LineCode,
                      Label,
                      SortOrder,
                      SUM(Amount) AS Amount
                  FROM cash_movement_rows
                  GROUP BY Section, LineCode, Label, SortOrder
                  ORDER BY Section, SortOrder, LineCode;
                  """;

        var rows = await QueryRowsAsync<CashMovementRow>(
            sql,
            new
            {
                FromUtc = fromUtc,
                ToExclusiveUtc = toExclusiveUtc,
                CashEquivalentRole = (short)CashFlowRole.CashEquivalent,
                IndirectMethod = (short)CashFlowMethod.Indirect,
                CounterpartyRoles
            },
            ct);

        return rows
            .Select(x => new CashFlowIndirectSnapshotLine(
                x.Section,
                x.LineCode,
                x.Label,
                x.SortOrder,
                x.Amount))
            .ToArray();
    }

    private async Task<IReadOnlyList<CashFlowIndirectUnclassifiedCashRow>> LoadUnclassifiedCashRowsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct)
    {
        var fromUtc = StartOfDayUtc(fromInclusive);
        var toExclusiveUtc = ToExclusiveUtc(toInclusive);

        var sql = """
                  WITH cash_rows AS (
                      SELECT
                          counter.code AS AccountCode,
                          counter.name AS AccountName,
                          SUM(r.amount) AS Amount
                      FROM accounting_register_main r
                      JOIN accounting_accounts cash
                        ON cash.account_id = r.debit_account_id
                       AND cash.is_deleted = FALSE
                       AND cash.cash_flow_role = @CashEquivalentRole
                      JOIN accounting_accounts counter
                        ON counter.account_id = r.credit_account_id
                       AND counter.is_deleted = FALSE
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                        AND counter.statement_section = ANY(@NonPnlSections::smallint[])
                        AND counter.cash_flow_role NOT IN (@CashEquivalentRole, @WorkingCapitalRole, @InvestingCounterpartyRole, @FinancingCounterpartyRole)
                      GROUP BY counter.code, counter.name

                      UNION ALL

                      SELECT
                          counter.code AS AccountCode,
                          counter.name AS AccountName,
                          SUM(-r.amount) AS Amount
                      FROM accounting_register_main r
                      JOIN accounting_accounts cash
                        ON cash.account_id = r.credit_account_id
                       AND cash.is_deleted = FALSE
                       AND cash.cash_flow_role = @CashEquivalentRole
                      JOIN accounting_accounts counter
                        ON counter.account_id = r.debit_account_id
                       AND counter.is_deleted = FALSE
                      WHERE r.period >= @FromUtc
                        AND r.period < @ToExclusiveUtc
                        AND counter.statement_section = ANY(@NonPnlSections::smallint[])
                        AND counter.cash_flow_role NOT IN (@CashEquivalentRole, @WorkingCapitalRole, @InvestingCounterpartyRole, @FinancingCounterpartyRole)
                      GROUP BY counter.code, counter.name
                  )
                  SELECT
                      AccountCode,
                      AccountName,
                      SUM(Amount) AS Amount
                  FROM cash_rows
                  GROUP BY AccountCode, AccountName
                  HAVING SUM(Amount) <> 0
                  ORDER BY AccountCode;
                  """;

        return await QueryRowsAsync<CashFlowIndirectUnclassifiedCashRow>(
            sql,
            new
            {
                FromUtc = fromUtc,
                ToExclusiveUtc = toExclusiveUtc,
                NonPnlSections,
                CashEquivalentRole = (short)CashFlowRole.CashEquivalent,
                WorkingCapitalRole = (short)CashFlowRole.WorkingCapital,
                InvestingCounterpartyRole = (short)CashFlowRole.InvestingCounterparty,
                FinancingCounterpartyRole = (short)CashFlowRole.FinancingCounterparty
            },
            ct);
    }

    private async Task<Dictionary<string, CashFlowLineDefinition>> LoadLineDefinitionsAsync(CancellationToken ct)
    {
        const string sql = """
                           SELECT
                               line_code AS LineCode,
                               method AS Method,
                               section AS Section,
                               label AS Label,
                               sort_order AS SortOrder,
                               is_system AS IsSystem
                           FROM accounting_cash_flow_lines
                           WHERE method = @IndirectMethod
                           ORDER BY section, sort_order, line_code;
                           """;

        var rows = await QueryRowsAsync<LineDefinitionRow>(
            sql,
            new { IndirectMethod = (short)CashFlowMethod.Indirect },
            ct);

        return rows.ToDictionary(
            x => x.LineCode,
            x => new CashFlowLineDefinition(
                x.LineCode,
                (CashFlowMethod)x.Method,
                (CashFlowSection)x.Section,
                x.Label,
                x.SortOrder,
                x.IsSystem),
            StringComparer.Ordinal);
    }

    private CashFlowLineDefinition ResolveLine(
        IReadOnlyDictionary<string, CashFlowLineDefinition> lines,
        string? lineCode,
        string accountCode)
    {
        if (string.IsNullOrWhiteSpace(lineCode) || !lines.TryGetValue(lineCode, out var line))
            throw new NgbInvariantViolationException($"Cash flow line definition is missing for account '{accountCode}'.");

        return line;
    }

    private static void AddAmount(
        IDictionary<string, MutableLine> target,
        CashFlowLineDefinition definition,
        decimal amount)
    {
        if (amount == 0m)
            return;

        if (!target.TryGetValue(definition.LineCode, out var current))
        {
            current = new MutableLine(definition.LineCode, definition.Label, definition.SortOrder);
            target[definition.LineCode] = current;
        }

        current.Amount += amount;
    }

    private static void AddAmount(IDictionary<string, MutableLine> target, MutableLine source, decimal amount)
    {
        if (amount == 0m)
            return;

        if (!target.TryGetValue(source.LineCode, out var current))
        {
            current = new MutableLine(source.LineCode, source.Label, source.SortOrder);
            target[source.LineCode] = current;
        }

        current.Amount += amount;
    }

    private async Task<IReadOnlyList<T>> QueryRowsAsync<T>(string sql, object args, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QueryAsync<T>(
            new CommandDefinition(
                sql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();
    }

    private static DateOnly StartOfMonth(DateOnly value) => new(value.Year, value.Month, 1);

    private static DateOnly EndOfMonth(DateOnly value) => new(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month));

    private static DateTime StartOfDayUtc(DateOnly value)
        => new(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime ToExclusiveUtc(DateOnly value)
        => StartOfDayUtc(value).AddDays(1);

    private static DateTime StartOfNextMonthUtc(DateOnly monthPeriod)
        => new DateTime(monthPeriod.Year, monthPeriod.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

    private static int CountPeriods(DateOnly fromInclusive, DateOnly toInclusive)
    {
        if (fromInclusive > toInclusive)
            return 0;

        return (toInclusive.Year - fromInclusive.Year) * 12 + toInclusive.Month - fromInclusive.Month + 1;
    }

    private sealed class LatestClosedPeriodsRow
    {
        public DateOnly? BeginningLatestClosedPeriod { get; init; }
        public DateOnly? EndingLatestClosedPeriod { get; init; }
    }

    private sealed class BalanceStateRow
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public StatementSection StatementSection { get; init; }
        public CashFlowRole CashFlowRole { get; init; }
        public string? CashFlowLineCode { get; init; }
        public decimal ClosingBalance { get; init; }
    }

    private sealed class ProfitAndLossRangeRow
    {
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public StatementSection StatementSection { get; init; }
        public CashFlowRole CashFlowRole { get; init; }
        public string? CashFlowLineCode { get; init; }
        public decimal NetMovement { get; init; }
    }

    private sealed class CashMovementRow
    {
        public CashFlowSection Section { get; init; }
        public string LineCode { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class LineDefinitionRow
    {
        public string LineCode { get; init; } = string.Empty;
        public short Method { get; init; }
        public short Section { get; init; }
        public string Label { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public bool IsSystem { get; init; }
    }

    private sealed class MutableBalanceRow(
        Guid accountId,
        string accountCode,
        string accountName,
        StatementSection statementSection,
        CashFlowRole cashFlowRole,
        string? cashFlowLineCode)
    {
        public Guid AccountId { get; } = accountId;
        public string AccountCode { get; } = accountCode;
        public string AccountName { get; } = accountName;
        public StatementSection StatementSection { get; } = statementSection;
        public CashFlowRole CashFlowRole { get; } = cashFlowRole;
        public string? CashFlowLineCode { get; } = cashFlowLineCode;
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
    }

    private sealed class MutableLine(string lineCode, string label, int sortOrder)
    {
        public string LineCode { get; } = lineCode;
        public string Label { get; } = label;
        public int SortOrder { get; } = sortOrder;
        public decimal Amount { get; set; }
    }
}
