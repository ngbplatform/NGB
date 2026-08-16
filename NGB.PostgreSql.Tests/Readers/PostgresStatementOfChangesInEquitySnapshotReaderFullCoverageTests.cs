using System.Data;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresStatementOfChangesInEquitySnapshotReaderFullCoverageTests
{
    private static readonly Guid FirstAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Get_validates_range_and_month_boundaries()
    {
        var sut = Fixture(null, null).Reader;
        Func<Task> reversed = () => sut.GetAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1));
        Func<Task> invalidFrom = () => sut.GetAsync(new DateOnly(2026, 8, 2), new DateOnly(2026, 9, 1));
        Func<Task> invalidTo = () => sut.GetAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 2));

        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Without_closed_periods_loads_inception_to_date_and_reports_zero_roll_forward_counts()
    {
        var fixture = Fixture(null, null);

        var snapshot = await fixture.Reader.GetAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1));

        snapshot.Rows.Should().BeEmpty();
        snapshot.OpeningLatestClosedPeriod.Should().BeNull();
        snapshot.ClosingLatestClosedPeriod.Should().BeNull();
        snapshot.OpeningRollForwardPeriods.Should().Be(0);
        snapshot.ClosingRollForwardPeriods.Should().Be(0);
        fixture.Connection.Commands.Should().HaveCount(3);
        fixture.Connection.Commands.Skip(1).Should().OnlyContain(
            command => command.CommandText.Contains("FROM accounting_turnovers t", StringComparison.Ordinal)
                       && !command.CommandText.Contains("snapshot_rows", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exact_opening_snapshot_and_closing_delta_merge_duplicate_and_new_accounts_and_sort_ties()
    {
        var openingPeriod = new DateOnly(2026, 7, 1);
        var openingRows = new[]
        {
            State(FirstAccountId, "100", "Zebra", StatementSection.Equity, 10m),
            State(FirstAccountId, "100", "Zebra", StatementSection.Equity, 2m),
            State(SecondAccountId, "100", "Alpha", StatementSection.Equity, 5m)
        };
        var closingRows = new[]
        {
            State(FirstAccountId, "100", "Zebra", StatementSection.Equity, 13m),
            State(ThirdAccountId, "200", "Retained earnings", StatementSection.Income, 7m)
        };
        var fixture = Fixture(openingPeriod, openingPeriod, openingRows, closingRows);

        var snapshot = await fixture.Reader.GetAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1));

        snapshot.Rows.Select(row => row.AccountId).Should().Equal(SecondAccountId, FirstAccountId, ThirdAccountId);
        snapshot.Rows.Single(row => row.AccountId == FirstAccountId).OpeningBalance.Should().Be(12);
        snapshot.Rows.Single(row => row.AccountId == FirstAccountId).ClosingBalance.Should().Be(13);
        snapshot.Rows.Single(row => row.AccountId == SecondAccountId).ClosingBalance.Should().Be(0);
        snapshot.Rows.Single(row => row.AccountId == ThirdAccountId).OpeningBalance.Should().Be(0);
        snapshot.OpeningRollForwardPeriods.Should().Be(0);
        snapshot.ClosingRollForwardPeriods.Should().Be(2);
        fixture.Connection.Commands[1].CommandText.Should().Contain("FROM accounting_balances b");
        fixture.Connection.Commands[1].CommandText.Should().NotContain("snapshot_rows");
        fixture.Connection.Commands[2].CommandText.Should().Contain("WITH snapshot_rows AS");
        fixture.Connection.Commands[2].CommandText.Should().Contain("delta_rows AS");
    }

    private static FixtureState Fixture(
        DateOnly? openingLatestClosed,
        DateOnly? closingLatestClosed,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? snapshotRows = null,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? deltaRows = null)
        => new(openingLatestClosed, closingLatestClosed, snapshotRows ?? [], deltaRows ?? []);

    private static IReadOnlyDictionary<string, object?> State(
        Guid accountId,
        string code,
        string name,
        StatementSection section,
        decimal closingBalance)
        => new Dictionary<string, object?>
        {
            ["AccountId"] = accountId,
            ["AccountCode"] = code,
            ["AccountName"] = name,
            ["StatementSection"] = (short)section,
            ["ClosingBalance"] = closingBalance
        };

    private sealed class FixtureState(
        DateOnly? openingLatestClosed,
        DateOnly? closingLatestClosed,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> snapshotRows,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> deltaRows)
    {
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: sql => sql.Contains("FROM accounting_closed_periods", StringComparison.Ordinal)
                ? Rows(
                [
                    new Dictionary<string, object?>
                    {
                        ["OpeningLatestClosedPeriod"] = openingLatestClosed,
                        ["ClosingLatestClosedPeriod"] = closingLatestClosed
                    }
                ])
                : sql.Contains("WITH snapshot_rows AS", StringComparison.Ordinal)
                    ? Rows(deltaRows)
                    : sql.Contains("FROM accounting_balances b", StringComparison.Ordinal)
                        ? Rows(snapshotRows)
                        : Rows([]));

        public PostgresStatementOfChangesInEquitySnapshotReader Reader => new(
            new RecordingUnitOfWork(Connection));
    }

    private static System.Data.Common.DbDataReader Rows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        foreach (var column in rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(column, typeof(object));

        foreach (var values in rows)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
                row[column] = values.TryGetValue(column.ColumnName, out var value) && value is not null
                    ? value
                    : DBNull.Value;
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }
}
