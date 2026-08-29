using System.Data;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.PostgreSql.Writers;
using Xunit;

namespace NGB.PostgreSql.Tests.Writers;

public sealed class PostgresAccountingBalanceProjectionWriterFullCoverageTests
{
    private static readonly DateOnly Period = new(2026, 8, 1);
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DimensionSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Project_requires_an_active_business_transaction()
    {
        var sut = new PostgresAccountingBalanceProjectionWriter(new RecordingUnitOfWork(new RecordingDbConnection()));

        await ((Func<Task>)(() => sut.ProjectAsync(Period, false, default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Project_stops_before_write_when_forbidden_negative_balances_exist()
    {
        var connection = Connection(
            forbiddenCount: 2,
            warningCount: 1,
            samples:
            [
                Sample(NegativeBalancePolicy.Forbid, -10m),
                Sample(NegativeBalancePolicy.Warn, -5m)
            ]);
        var sut = Writer(connection);

        var result = await sut.ProjectAsync(Period, replaceExisting: false, default);

        result.RowsWritten.Should().Be(0);
        result.ForbiddenCount.Should().Be(2);
        result.WarningCount.Should().Be(1);
        result.ViolationSamples.Should().HaveCount(2);
        connection.Commands.Should().HaveCount(2);
        connection.Commands.Should().NotContain(command =>
            command.CommandText.Contains("INSERT INTO accounting_balances", StringComparison.Ordinal));
        connection.Commands.Last().CommandText.Should().Contain("LIMIT @MaxViolationSamples");
    }

    [Theory]
    [InlineData(false, "ON CONFLICT (period, account_id, dimension_set_id)")]
    [InlineData(true, "DELETE FROM accounting_balances WHERE period = @Period")]
    public async Task Project_writes_candidates_set_wise_and_returns_bounded_warnings(
        bool replaceExisting,
        string expectedWriteClause)
    {
        var connection = Connection(
            forbiddenCount: 0,
            warningCount: 1,
            samples: [Sample(NegativeBalancePolicy.Warn, -5m)],
            rowsWritten: 3);
        var sut = Writer(connection);

        var result = await sut.ProjectAsync(Period, replaceExisting, default);

        result.RowsWritten.Should().Be(3);
        result.ForbiddenCount.Should().Be(0);
        result.WarningCount.Should().Be(1);
        result.ViolationSamples.Should().ContainSingle(x => x.Policy == NegativeBalancePolicy.Warn);
        connection.Commands.Should().HaveCount(3);
        connection.Commands[0].CommandText.Should()
            .Contain("ngb_balance_projection_candidate")
            .And.Contain("UNION")
            .And.Contain("accounting_turnovers");
        connection.Commands[2].CommandText.Should().Contain(expectedWriteClause)
            .And.Contain("SELECT COUNT(*)::int FROM ngb_balance_projection_candidate");
    }

    private static PostgresAccountingBalanceProjectionWriter Writer(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static RecordingDbConnection Connection(
        long forbiddenCount,
        long warningCount,
        IReadOnlyList<SampleRow> samples,
        int rowsWritten = 0)
        => new(
            readerFactory: sql => sql.Contains("COUNT(*) FILTER", StringComparison.Ordinal)
                ? ViolationResults(forbiddenCount, warningCount, samples)
                : new DataTable().CreateDataReader(),
            scalar: sql => sql.Contains("SELECT COUNT(*)::int", StringComparison.Ordinal)
                ? rowsWritten
                : null);

    private static SampleRow Sample(NegativeBalancePolicy policy, decimal closingBalance)
        => new(policy, closingBalance);

    private static System.Data.Common.DbDataReader ViolationResults(
        long forbiddenCount,
        long warningCount,
        IReadOnlyList<SampleRow> samples)
    {
        var counts = new DataTable();
        counts.Columns.Add("ForbiddenCount", typeof(long));
        counts.Columns.Add("WarningCount", typeof(long));
        counts.Rows.Add(forbiddenCount, warningCount);

        var rows = new DataTable();
        rows.Columns.Add("Period", typeof(DateOnly));
        rows.Columns.Add("AccountId", typeof(Guid));
        rows.Columns.Add("AccountCode", typeof(string));
        rows.Columns.Add("AccountName", typeof(string));
        rows.Columns.Add("AccountType", typeof(int));
        rows.Columns.Add("Policy", typeof(int));
        rows.Columns.Add("DimensionSetId", typeof(Guid));
        rows.Columns.Add("ClosingBalance", typeof(decimal));
        foreach (var sample in samples)
        {
            rows.Rows.Add(
                Period,
                AccountId,
                "1000",
                "Cash",
                (int)AccountType.Asset,
                (int)sample.Policy,
                DimensionSetId,
                sample.ClosingBalance);
        }

        var dataSet = new DataSet();
        dataSet.Tables.Add(counts);
        dataSet.Tables.Add(rows);
        return dataSet.CreateDataReader();
    }

    private sealed record SampleRow(NegativeBalancePolicy Policy, decimal ClosingBalance);
}
