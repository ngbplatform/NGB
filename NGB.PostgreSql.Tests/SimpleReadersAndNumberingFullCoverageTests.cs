using System.Data;
using FluentAssertions;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Documents.Numbering;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class SimpleReadersAndNumberingFullCoverageTests
{
    [Fact]
    public async Task Account_lookup_validates_filters_deduplicates_and_materializes_rows()
    {
        var accountId = Guid.NewGuid();
        var data = Table(
            [("AccountId", typeof(Guid)), ("Code", typeof(string)), ("Name", typeof(string))],
            [accountId, "1010", "Cash"]);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = new PostgresAccountLookupReader(new RecordingUnitOfWork(connection));
        Func<Task> missing = async () => await sut.GetByIdsAsync(null!, default);
        await missing.Should().ThrowAsync<NgbArgumentRequiredException>();

        (await sut.GetByIdsAsync([], default)).Should().BeEmpty();
        (await sut.GetByIdsAsync([Guid.Empty, Guid.Empty], default)).Should().BeEmpty();
        var rows = await sut.GetByIdsAsync([Guid.Empty, accountId, accountId], default);

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            AccountId = accountId,
            Code = "1010",
            Name = "Cash"
        });
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("account_id = ANY(").And.Contain("ORDER BY code");
    }

    [Fact]
    public async Task Retained_earnings_lookup_validates_limits_and_handles_null_blank_and_trimmed_queries()
    {
        var data = Table(
            [("AccountId", typeof(Guid)), ("Code", typeof(string)), ("Name", typeof(string))],
            [Guid.NewGuid(), "3000", "Retained Earnings"]);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = new PostgresRetainedEarningsAccountLookupReader(new RecordingUnitOfWork(connection));

        foreach (var invalid in new[] { 0, -1, 101 })
        {
            Func<Task> act = async () => await sut.SearchAsync("equity", invalid, default);
            await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        }

        (await sut.SearchAsync(null, 1, default)).Should().ContainSingle();
        (await sut.SearchAsync("  ", 100, default)).Should().ContainSingle();
        (await sut.SearchAsync("  earnings  ", 10, default)).Should().ContainSingle();

        connection.Commands.Should().HaveCount(3);
        connection.Commands.Should().OnlyContain(x =>
            x.CommandText.Contains("statement_section", StringComparison.Ordinal)
            && x.CommandText.Contains("ILIKE", StringComparison.Ordinal));
        connection.Commands[0].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "Pattern" && Equals(x.Value, DBNull.Value));
        connection.Commands[2].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "Pattern" && Equals(x.Value, "%earnings%"));
    }

    [Fact]
    public async Task Closed_period_reader_validates_boundaries_empty_ranges_rows_and_scalars()
    {
        var closedAt = new DateTime(2026, 8, 16, 1, 2, 3, DateTimeKind.Utc);
        var rowsTable = Table(
            [("Period", typeof(DateOnly)), ("ClosedBy", typeof(string)), ("ClosedAtUtc", typeof(DateTime))],
            [new DateOnly(2026, 8, 1), "unit", closedAt]);
        var connection = new RecordingDbConnection(
            _ => rowsTable.CreateDataReader(),
            scalar: sql => sql.Contains("MAX(period)", StringComparison.Ordinal)
                ? new DateOnly(2026, 8, 1)
                : true);
        var sut = new PostgresClosedPeriodReader(new RecordingUnitOfWork(connection));

        Func<Task> invalidFrom = async () => await sut.GetClosedAsync(
            new DateOnly(2026, 8, 2), new DateOnly(2026, 9, 1), default);
        Func<Task> invalidTo = async () => await sut.GetClosedAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 2), default);
        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        (await sut.GetClosedAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), default))
            .Should().BeEmpty();
        var rows = await sut.GetClosedAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), default);
        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Period = new DateOnly(2026, 8, 1),
            ClosedBy = "unit",
            ClosedAtUtc = closedAt
        });
        (await sut.GetLatestClosedPeriodAsync(default)).Should().Be(new DateOnly(2026, 8, 1));
        (await sut.ExistsClosedAfterAsync(new DateOnly(2026, 7, 1), default)).Should().BeTrue();
    }

    [Fact]
    public async Task Turnover_aggregation_executes_single_period_and_range_queries()
    {
        var period = new DateOnly(2026, 8, 1);
        var accountId = Guid.NewGuid();
        var dimensionSetId = Guid.NewGuid();
        var data = Table(
            [
                ("Period", typeof(DateOnly)),
                ("AccountId", typeof(Guid)),
                ("DimensionSetId", typeof(Guid)),
                ("AccountCode", typeof(string)),
                ("DebitAmount", typeof(decimal)),
                ("CreditAmount", typeof(decimal))
            ],
            [period, accountId, dimensionSetId, "1010", 12m, 3m]);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = new PostgresAccountingTurnoverAggregationReader(new RecordingUnitOfWork(connection));

        var single = await sut.GetAggregatedFromRegisterAsync(period, default);
        var range = await sut.GetAggregatedFromRegisterRangeAsync(period, period.AddMonths(1), default);

        single.Should().ContainSingle();
        range.Should().BeEquivalentTo(single);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should().Contain("period_month = @Period");
        connection.Commands[1].CommandText.Should().Contain("period_month BETWEEN @From AND @To");
    }

    [Fact]
    public async Task Dimension_definition_reader_validates_normalizes_deduplicates_and_materializes()
    {
        var id = Guid.NewGuid();
        var data = Table(
            [("CodeNorm", typeof(string)), ("DimensionId", typeof(Guid))],
            ["warehouse", id]);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = new PostgresDimensionDefinitionReader(new RecordingUnitOfWork(connection));
        Func<Task> missing = async () => await sut.GetDimensionIdsByCodesAsync(null!, default);
        await missing.Should().ThrowAsync<NgbArgumentRequiredException>();

        (await sut.GetDimensionIdsByCodesAsync([], default)).Should().BeEmpty();
        (await sut.GetDimensionIdsByCodesAsync(["", "  "], default)).Should().BeEmpty();
        var result = await sut.GetDimensionIdsByCodesAsync(
            [" Warehouse ", "warehouse", "", "WAREHOUSE"], default);

        result.Should().Contain("WAREHOUSE", id);
        connection.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Document_number_sequence_validates_boundaries_requires_transaction_and_returns_scalar()
    {
        var connection = new RecordingDbConnection(scalar: _ => 42L);
        var active = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var sut = new PostgresDocumentNumberSequenceRepository(active);
        Func<Task> blank = async () => await sut.NextAsync(" ", 2026, default);
        await blank.Should().ThrowAsync<NgbArgumentRequiredException>();

        foreach (var invalid in new[] { 1899, 3001 })
        {
            Func<Task> act = async () => await sut.NextAsync("invoice", invalid, default);
            await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        }

        (await sut.NextAsync("invoice", 1900, default)).Should().Be(42);
        (await sut.NextAsync("invoice", 3000, default)).Should().Be(42);
        connection.Commands.Should().HaveCount(2)
            .And.OnlyContain(x => x.CommandText.Contains("RETURNING last_seq", StringComparison.Ordinal));

        var inactive = new PostgresDocumentNumberSequenceRepository(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => 1L)));
        Func<Task> noTransaction = async () => await inactive.NextAsync("invoice", 2026, default);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>();
    }

    private static DataTable Table(
        IReadOnlyList<(string Name, Type Type)> columns,
        params object?[] values)
    {
        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column.Name, column.Type);
        table.Rows.Add(values);
        return table;
    }
}
