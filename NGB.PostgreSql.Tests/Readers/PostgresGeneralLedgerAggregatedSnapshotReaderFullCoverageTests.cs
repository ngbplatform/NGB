using System.Data;
using FluentAssertions;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresGeneralLedgerAggregatedSnapshotReaderFullCoverageTests
{
    [Fact]
    public async Task Get_validates_account_range_and_month_boundaries()
    {
        var sut = new PostgresGeneralLedgerAggregatedSnapshotReader(null!);
        Func<Task> emptyAccount = async () => await sut.GetAsync(
            Guid.Empty, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), null, default);
        Func<Task> reversed = async () => await sut.GetAsync(
            Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), null, default);
        Func<Task> invalidFrom = async () => await sut.GetAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 2), new DateOnly(2026, 9, 1), null, default);
        Func<Task> invalidTo = async () => await sut.GetAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 2), null, default);

        await emptyAccount.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Get_uses_inception_query_when_no_closed_period_exists_and_normalizes_null_account_code()
    {
        var (sut, connection) = Create(latestClosed: null, accountCode: null, 10m, 20m, 3m);

        var result = await sut.GetAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), null, default);

        result.AccountCode.Should().BeEmpty();
        result.OpeningBalance.Should().Be(10m);
        result.TotalDebit.Should().Be(20m);
        result.TotalCredit.Should().Be(3m);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[1].CommandText.Should().Contain("summary AS")
            .And.NotContain("opening_snapshot AS");
    }

    [Fact]
    public async Task Get_uses_snapshot_only_when_closed_period_equals_range_start()
    {
        var period = new DateOnly(2026, 8, 1);
        var (sut, connection) = Create(period, "1010", 11m, 21m, 4m);

        var result = await sut.GetAsync(Guid.NewGuid(), period, period.AddMonths(1), null, default);

        result.AccountCode.Should().Be("1010");
        connection.Commands[1].CommandText.Should().Contain("SUM(b.opening_balance)")
            .And.Contain("range_totals AS");
    }

    [Fact]
    public async Task Get_uses_snapshot_plus_delta_when_closed_period_precedes_range_start()
    {
        var (sut, connection) = Create(new DateOnly(2026, 7, 1), "1010", 12m, 22m, 5m);

        var result = await sut.GetAsync(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), null, default);

        result.OpeningBalance.Should().Be(12m);
        connection.Commands[1].CommandText.Should().Contain("SnapshotClosingBalance")
            .And.Contain("turnover_delta AS");
    }

    private static (PostgresGeneralLedgerAggregatedSnapshotReader Sut, RecordingDbConnection Connection) Create(
        DateOnly? latestClosed,
        string? accountCode,
        decimal opening,
        decimal debit,
        decimal credit)
    {
        var row = new DataTable();
        row.Columns.Add("AccountCode", typeof(string));
        row.Columns.Add("OpeningBalance", typeof(decimal));
        row.Columns.Add("TotalDebit", typeof(decimal));
        row.Columns.Add("TotalCredit", typeof(decimal));
        row.Rows.Add(accountCode ?? (object)DBNull.Value, opening, debit, credit);
        var connection = new RecordingDbConnection(
            _ => row.CreateDataReader(),
            scalar: _ => latestClosed ?? (object)DBNull.Value);
        return (
            new PostgresGeneralLedgerAggregatedSnapshotReader(new RecordingUnitOfWork(connection)),
            connection);
    }
}
