using System.Data;
using System.Data.Common;
using FluentAssertions;
using Moq;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class ReportReadLifecycleTests
{
    [Fact]
    public void Read_path_migration_reports_missing_packaged_sql_and_loads_the_released_resource()
    {
        var migration = new NGB.PostgreSql.Migrations.Platform.PlatformReadPathIndexesMigration();
        migration.Generate().Should().Contain("INDEX");
        Action missing = () => NGB.PostgreSql.Migrations.Platform.PlatformReadPathIndexesMigration.ReadEmbeddedSql("missing.sql");
        missing.Should().Throw<NgbInvariantViolationException>().WithMessage("*missing.sql*");
    }

    [Fact]
    public async Task Existing_transaction_is_preserved_and_ending_an_unstarted_session_is_harmless()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        var session = new PostgresReportReadSession(uow.Object);
        await session.EndAsync(default);
        await ((Func<Task>)(() => session.BeginAsync(default))).Should().ThrowAsync<InvalidOperationException>();
        session.SnapshotId.Should().BeEmpty();
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Isolation_setup_failure_rolls_back_once_without_a_snapshot_or_cancelled_cleanup_token()
    {
        var connection = new RecordingDbConnection(nonQuery: _ => throw new IOException("SET failed"));
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Connection).Returns(connection);
        var session = new PostgresReportReadSession(uow.Object);
        await ((Func<Task>)(() => session.BeginAsync(default))).Should().ThrowAsync<IOException>();
        await session.EndAsync(default);
        session.SnapshotId.Should().BeEmpty();
        uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cursor_cleanup_failure_does_not_replace_successful_rows(bool dataset)
    {
        var data = new DataTable();
        data.Columns.Add("value", typeof(int));
        data.Rows.Add(42);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader(), nonQuery: sql => sql.StartsWith("CLOSE ") ? throw new CursorCloseException() : 1);
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var actual = new List<int>();
        if (dataset)
        {
            var source = new Dataset();
            var executor = new PostgresReportDatasetExecutor(uow, new(new([source])));
            await foreach (var page in executor.ReadAsync(new("test", [], [], [new("value", "value", "Value", "int32")], [], [], [], new Dictionary<string, object?>(), new(0, 10)), default))
                actual.AddRange(page.Rows.Select(row => (int)row.Values["value"]!));
        }
        else
        {
            await foreach (var batch in PostgresReportCursorStream.ReadAsync<int>(uow, "SELECT 42", null, default)) actual.AddRange(batch);
        }
        actual.Should().Equal(42);
        connection.Commands.Count(c => c.CommandText.StartsWith("CLOSE ")).Should().Be(1);
    }

    [Fact]
    public async Task Accounting_streams_reject_reversed_ranges_before_opening_connection()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var from = new DateOnly(2026, 9, 1);
        await using var statement = new PostgresAccountingStatementAccountReader(uow.Object).ReadAsync(from, from.AddMonths(-1), null, default).GetAsyncEnumerator();
        await using var trial = new PostgresTrialBalanceAccountSummaryReader(uow.Object).ReadAsync(from, from.AddMonths(-1), null).GetAsyncEnumerator();
        await ((Func<Task>)(async () => await statement.MoveNextAsync())).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(async () => await trial.MoveNextAsync())).Should().ThrowAsync<NgbArgumentInvalidException>();
        var summary = new PostgresAccountingSummaryPageReader(uow.Object);
        await ((Func<Task>)(() => summary.ReadGroupsAsync(new((AccountingSummaryKind)99, from, from, null), default)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Valid_accounting_ranges_and_consistency_previous_month_are_forwarded()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var month = new DateOnly(2026, 9, 1);
        await foreach (var _ in new PostgresAccountingStatementAccountReader(uow).ReadAsync(month, month, null, default)) { }
        await foreach (var _ in new PostgresTrialBalanceAccountSummaryReader(uow).ReadAsync(month, month, null)) { }
        foreach (DateOnly? previous in new DateOnly?[] { null, month.AddMonths(-1) })
            await foreach (var _ in new PostgresAccountingConsistencySnapshotReader(uow).ReadAsync(month, previous, default)) { }
        foreach (var kind in new[] { AccountingSummaryKind.BalanceSheet, AccountingSummaryKind.IncomeStatement, AccountingSummaryKind.Equity })
            (await new PostgresAccountingSummaryPageReader(uow).ReadGroupsAsync(new(kind, month, month, null), default)).Should().BeEmpty();
        connection.Commands.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_database_cursor_cleanup_failures_are_not_silenced(bool dataset)
    {
        var connection = new RecordingDbConnection(nonQuery: sql => sql.StartsWith("CLOSE ") ? throw new IOException("close failed") : 1);
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var action = async () =>
        {
            if (dataset)
            {
                var executor = new PostgresReportDatasetExecutor(uow, new(new([new Dataset()])));
                await foreach (var _ in executor.ReadAsync(new("test", [], [], [new("value", "value", "Value", "int32")], [], [], [], new Dictionary<string, object?>(), new(0, 10)), default)) { }
            }
            else await foreach (var _ in PostgresReportCursorStream.ReadAsync<int>(uow, "SELECT 42", null, default)) { }
        };
        await action.Should().ThrowAsync<IOException>().WithMessage("close failed");
    }

    private sealed class CursorCloseException : DbException;
    private sealed class Dataset : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [new("test", "test_rows", [new("value", "value", "int32")], [])];
    }
}
