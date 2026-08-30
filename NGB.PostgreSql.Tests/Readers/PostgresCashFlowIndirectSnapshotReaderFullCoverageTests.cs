using System.Data;
using FluentAssertions;
using NGB.Accounting.CashFlow;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresCashFlowIndirectSnapshotReaderFullCoverageTests
{
    [Fact]
    public async Task Get_rejects_reversed_ranges()
    {
        var sut = new PostgresCashFlowIndirectSnapshotReader(null!);
        Func<Task> act = async () => await sut.GetAsync(
            new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1), default);
        await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Get_builds_complete_inception_snapshot_with_positive_negative_zero_and_merged_lines()
    {
        var cash = Guid.NewGuid();
        var working = Guid.NewGuid();
        var closingOnly = Guid.NewGuid();
        var openingOnly = Guid.NewGuid();
        var other = Guid.NewGuid();
        var scenario = new CashFlowScenario(
            latestClosed: LatestClosed(null, null),
            lineDefinitions: LineDefinitions(),
            endpointRows: EndpointBalanceRows(
                (cash, "1000", CashFlowRole.CashEquivalent, null, 102m, 135m),
                (working, "1100", CashFlowRole.WorkingCapital, "wc", 50m, 40m),
                (openingOnly, "1200", CashFlowRole.WorkingCapital, "wc", 10m, 0m),
                (closingOnly, "1300", CashFlowRole.WorkingCapital, "wc", 0m, 10m),
                (other, "9999", CashFlowRole.None, null, 20m, 25m)),
            pnlRows: ProfitAndLossRows(
                ("4000", CashFlowRole.None, null, 20m),
                ("5000", CashFlowRole.NonCashOperatingAdjustment, "nc", 5m),
                ("5100", CashFlowRole.NonCashOperatingAdjustment, "wc", 2m),
                ("5200", CashFlowRole.NonCashOperatingAdjustment, "wc", 0m)),
            cashMovementRows: CashMovementRows(
                (CashFlowSection.Investing, "inv", "Investing", 2, 30m),
                (CashFlowSection.Financing, "fin", "Financing", 3, -10m),
                (CashFlowSection.Operating, "ignored", "Ignored", 4, 1m)),
            unclassifiedRows: UnclassifiedRows(("1999", "Other asset", 7m)));
        var sut = new PostgresCashFlowIndirectSnapshotReader(scenario.UnitOfWork);

        var result = await sut.GetAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), default);

        result.BeginningCash.Should().Be(102m);
        result.EndingCash.Should().Be(135m);
        result.NetIncome.Should().Be(27m);
        result.BeginningLatestClosedPeriod.Should().BeNull();
        result.EndingLatestClosedPeriod.Should().BeNull();
        result.BeginningRollForwardPeriods.Should().Be(0);
        result.EndingRollForwardPeriods.Should().Be(0);
        result.OperatingLines.Select(x => (x.LineCode, x.Amount)).Should()
            .Equal(("nc", -5m), ("wc", 8m));
        result.OperatingLines.Should().OnlyContain(x => x.Section == CashFlowSection.Operating);
        result.InvestingLines.Should().ContainSingle().Which.LineCode.Should().Be("inv");
        result.FinancingLines.Should().ContainSingle().Which.LineCode.Should().Be("fin");
        result.UnclassifiedCashRows.Should().ContainSingle().Which.AccountCode.Should().Be("1999");
        scenario.Commands.Should().HaveCount(6);
        scenario.Commands.Should().ContainSingle(x => x.CommandText.Contains("WITH endpoint_ledger_rows", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_uses_snapshot_only_for_closed_month_ends_and_zero_roll_forward_periods()
    {
        var scenario = new CashFlowScenario(
            LatestClosed(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)),
            LineDefinitions(),
            snapshotRows: [BalanceRows(), BalanceRows()],
            pnlRows: ProfitAndLossRows(),
            cashMovementRows: CashMovementRows(),
            unclassifiedRows: UnclassifiedRows());
        var sut = new PostgresCashFlowIndirectSnapshotReader(scenario.UnitOfWork);

        var result = await sut.GetAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        result.BeginningLatestClosedPeriod.Should().Be(new DateOnly(2026, 8, 1));
        result.EndingLatestClosedPeriod.Should().Be(new DateOnly(2026, 9, 1));
        result.BeginningRollForwardPeriods.Should().Be(0);
        result.EndingRollForwardPeriods.Should().Be(0);
        scenario.Commands.Count(x => x.CommandText.Contains("FROM accounting_balances b", StringComparison.Ordinal))
            .Should().Be(2);
    }

    [Fact]
    public async Task Get_uses_snapshot_plus_delta_and_counts_roll_forward_periods()
    {
        var scenario = new CashFlowScenario(
            LatestClosed(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            LineDefinitions(),
            deltaRows: [BalanceRows(), BalanceRows()],
            pnlRows: ProfitAndLossRows(),
            cashMovementRows: CashMovementRows(),
            unclassifiedRows: UnclassifiedRows());
        var sut = new PostgresCashFlowIndirectSnapshotReader(scenario.UnitOfWork);

        var result = await sut.GetAsync(
            new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 15), default);

        result.BeginningRollForwardPeriods.Should().Be(2);
        result.EndingRollForwardPeriods.Should().Be(2);
        scenario.Commands.Count(x => x.CommandText.Contains("WITH snapshot_rows", StringComparison.Ordinal))
            .Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public async Task Get_fails_when_working_capital_account_has_no_valid_line_definition(string? lineCode)
    {
        var scenario = new CashFlowScenario(
            LatestClosed(null, null),
            LineDefinitions(),
            endpointRows: EndpointBalanceRows((Guid.NewGuid(), "1100", CashFlowRole.WorkingCapital, lineCode, 10m, 0m)));
        var sut = new PostgresCashFlowIndirectSnapshotReader(scenario.UnitOfWork);

        Func<Task> act = async () => await sut.GetAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), default);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Cash flow line definition is missing for account '1100'*");
    }

    private sealed class CashFlowScenario
    {
        private readonly Queue<DataTable> _inceptionRows;
        private readonly DataTable _endpointRows;
        private readonly Queue<DataTable> _snapshotRows;
        private readonly Queue<DataTable> _deltaRows;
        private readonly DataTable _latestClosed;
        private readonly DataTable _lineDefinitions;
        private readonly DataTable _pnlRows;
        private readonly DataTable _cashMovementRows;
        private readonly DataTable _unclassifiedRows;

        public CashFlowScenario(
            DataTable latestClosed,
            DataTable lineDefinitions,
            DataTable? endpointRows = null,
            IEnumerable<DataTable>? inceptionRows = null,
            IEnumerable<DataTable>? snapshotRows = null,
            IEnumerable<DataTable>? deltaRows = null,
            DataTable? pnlRows = null,
            DataTable? cashMovementRows = null,
            DataTable? unclassifiedRows = null)
        {
            _latestClosed = latestClosed;
            _lineDefinitions = lineDefinitions;
            _endpointRows = endpointRows ?? EndpointBalanceRows();
            _inceptionRows = new Queue<DataTable>(inceptionRows ?? []);
            _snapshotRows = new Queue<DataTable>(snapshotRows ?? []);
            _deltaRows = new Queue<DataTable>(deltaRows ?? []);
            _pnlRows = pnlRows ?? ProfitAndLossRows();
            _cashMovementRows = cashMovementRows ?? CashMovementRows();
            _unclassifiedRows = unclassifiedRows ?? UnclassifiedRows();
            var connection = new RecordingDbConnection(sql => Select(sql).CreateDataReader());
            UnitOfWork = new RecordingUnitOfWork(connection);
            Commands = connection.Commands;
        }

        public RecordingUnitOfWork UnitOfWork { get; }
        public IReadOnlyList<RecordingDbCommand> Commands { get; }

        private DataTable Select(string sql)
        {
            if (sql.Contains("BeginningLatestClosedPeriod", StringComparison.Ordinal))
                return _latestClosed;
            if (sql.Contains("FROM accounting_cash_flow_lines", StringComparison.Ordinal)
                && !sql.Contains("JOIN accounting_cash_flow_lines", StringComparison.Ordinal))
                return _lineDefinitions;
            if (sql.Contains("WITH endpoint_ledger_rows", StringComparison.Ordinal))
                return _endpointRows;
            if (sql.Contains("WITH ledger_rows", StringComparison.Ordinal))
                return _inceptionRows.Dequeue();
            if (sql.Contains("WITH snapshot_rows", StringComparison.Ordinal))
                return _deltaRows.Dequeue();
            if (sql.Contains("FROM accounting_balances b", StringComparison.Ordinal))
                return _snapshotRows.Dequeue();
            if (sql.Contains("WITH range_rows", StringComparison.Ordinal))
                return _pnlRows;
            if (sql.Contains("WITH cash_movement_rows", StringComparison.Ordinal))
                return _cashMovementRows;
            if (sql.Contains("WITH cash_rows", StringComparison.Ordinal))
                return _unclassifiedRows;
            throw new InvalidOperationException($"Unexpected SQL: {sql}");
        }
    }

    private static DataTable LatestClosed(DateOnly? beginning, DateOnly? ending)
        => Table(
            [("BeginningLatestClosedPeriod", typeof(DateOnly)), ("EndingLatestClosedPeriod", typeof(DateOnly))],
            [beginning ?? (object)DBNull.Value, ending ?? (object)DBNull.Value]);

    private static DataTable LineDefinitions()
    {
        var table = CreateTable(
            ("LineCode", typeof(string)), ("Method", typeof(short)), ("Section", typeof(short)),
            ("Label", typeof(string)), ("SortOrder", typeof(int)), ("IsSystem", typeof(bool)));
        table.Rows.Add("wc", (short)CashFlowMethod.Indirect, (short)CashFlowSection.Operating, "Working capital", 2, false);
        table.Rows.Add("nc", (short)CashFlowMethod.Indirect, (short)CashFlowSection.Operating, "Non-cash", 1, true);
        return table;
    }

    private static DataTable BalanceRows(
        params (Guid Id, string Code, CashFlowRole Role, string? LineCode, decimal Balance)[] rows)
    {
        var table = CreateTable(
            ("AccountId", typeof(Guid)), ("AccountCode", typeof(string)),
            ("CashFlowRole", typeof(short)), ("CashFlowLineCode", typeof(string)),
            ("ClosingBalance", typeof(decimal)));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.Code, (short)row.Role, row.LineCode ?? (object)DBNull.Value, row.Balance);
        return table;
    }

    private static DataTable EndpointBalanceRows(
        params (Guid Id, string Code, CashFlowRole Role, string? LineCode, decimal Opening, decimal Closing)[] rows)
    {
        var table = CreateTable(
            ("AccountId", typeof(Guid)), ("AccountCode", typeof(string)),
            ("CashFlowRole", typeof(short)), ("CashFlowLineCode", typeof(string)),
            ("OpeningBalance", typeof(decimal)), ("ClosingBalance", typeof(decimal)));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                row.Code,
                (short)row.Role,
                row.LineCode ?? (object)DBNull.Value,
                row.Opening,
                row.Closing);
        }

        return table;
    }

    private static DataTable ProfitAndLossRows(
        params (string Code, CashFlowRole Role, string? LineCode, decimal Movement)[] rows)
    {
        var table = CreateTable(
            ("AccountCode", typeof(string)), ("CashFlowRole", typeof(short)),
            ("CashFlowLineCode", typeof(string)), ("NetMovement", typeof(decimal)));
        foreach (var row in rows)
            table.Rows.Add(row.Code, (short)row.Role, row.LineCode ?? (object)DBNull.Value, row.Movement);
        return table;
    }

    private static DataTable CashMovementRows(
        params (CashFlowSection Section, string Code, string Label, int Sort, decimal Amount)[] rows)
    {
        var table = CreateTable(
            ("Section", typeof(short)), ("LineCode", typeof(string)), ("Label", typeof(string)),
            ("SortOrder", typeof(int)), ("Amount", typeof(decimal)));
        foreach (var row in rows)
            table.Rows.Add((short)row.Section, row.Code, row.Label, row.Sort, row.Amount);
        return table;
    }

    private static DataTable UnclassifiedRows(
        params (string Code, string Name, decimal Amount)[] rows)
    {
        var table = CreateTable(
            ("AccountCode", typeof(string)), ("AccountName", typeof(string)), ("Amount", typeof(decimal)));
        foreach (var row in rows)
            table.Rows.Add(row.Code, row.Name, row.Amount);
        return table;
    }

    private static DataTable Table(
        IReadOnlyList<(string Name, Type Type)> columns,
        params object?[] values)
    {
        var table = CreateTable(columns.ToArray());
        table.Rows.Add(values);
        return table;
    }

    private static DataTable CreateTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column.Name, column.Type);
        return table;
    }
}
