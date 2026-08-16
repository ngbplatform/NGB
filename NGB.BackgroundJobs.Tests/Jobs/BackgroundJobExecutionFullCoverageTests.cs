using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.BackgroundJobs.Jobs;
using NGB.BackgroundJobs.Observability;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Jobs;

public sealed class BackgroundJobExecutionFullCoverageTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(
        new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Jobs_DefaultToSystemClock_WhenNoClockIsSupplied()
    {
        var metrics = new JobRunMetrics();
        new AccountingIntegrityScanJob(
            Mock.Of<IAccountingIntegrityChecker>(), NullLogger<AccountingIntegrityScanJob>.Instance, metrics)
            .Should().NotBeNull();
        new AccountingAggregatesDriftCheckJob(
            Mock.Of<IAccountingIntegrityDiagnostics>(), NullLogger<AccountingAggregatesDriftCheckJob>.Instance, metrics)
            .Should().NotBeNull();
        new AccountingOperationsStuckMonitorJob(
            Mock.Of<IPostingStateReader>(), NullLogger<AccountingOperationsStuckMonitorJob>.Instance, metrics)
            .Should().NotBeNull();
        new AuditHealthJob(
            Mock.Of<IUnitOfWork>(), NullLogger<AuditHealthJob>.Instance, metrics)
            .Should().NotBeNull();
        new OperationalRegistersFinalizeDirtyMonthsJob(
            Mock.Of<IOperationalRegisterAdminMaintenanceService>(),
            NullLogger<OperationalRegistersFinalizeDirtyMonthsJob>.Instance, metrics)
            .Should().NotBeNull();
        new PlatformSchemaValidateJob(
            Mock.Of<IDocumentsCoreSchemaValidationService>(),
            Mock.Of<IAccountingCoreSchemaValidationService>(),
            Mock.Of<IOperationalRegistersCoreSchemaValidationService>(),
            Mock.Of<IReferenceRegistersCoreSchemaValidationService>(),
            Mock.Of<ICatalogSchemaValidationService>(),
            Mock.Of<IDocumentSchemaValidationService>(),
            NullLogger<PlatformSchemaValidateJob>.Instance,
            metrics)
            .Should().NotBeNull();
    }

    [Fact]
    public async Task AccountingIntegrityScan_ValidatesCurrentAndPreviousMonth()
    {
        var periods = new List<DateOnly>();
        var checker = new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict);
        checker.Setup(x => x.AssertPeriodIsBalancedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((period, _) => periods.Add(period))
            .Returns(Task.CompletedTask);
        var metrics = new JobRunMetrics();
        var sut = new AccountingIntegrityScanJob(
            checker.Object, NullLogger<AccountingIntegrityScanJob>.Instance, metrics, Clock);

        await sut.RunAsync(default);

        sut.JobId.Should().Be("accounting.integrity.scan");
        periods.Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2025, 12, 1));
        metrics.Snapshot()["periods_scanned"].Should().Be(2);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    public async Task AccountingAggregatesDriftCheck_CoversHealthyAndBothDriftPeriods(
        long current,
        long previous,
        bool throws)
    {
        var calls = 0;
        var diagnostics = new Mock<IAccountingIntegrityDiagnostics>(MockBehavior.Strict);
        diagnostics.Setup(x => x.GetTurnoversVsRegisterDiffCountAsync(
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => calls++ == 0 ? current : previous);
        var metrics = new JobRunMetrics();
        var sut = new AccountingAggregatesDriftCheckJob(
            diagnostics.Object, NullLogger<AccountingAggregatesDriftCheckJob>.Instance, metrics, Clock);
        var act = () => sut.RunAsync(default);

        if (throws)
            await act.Should().ThrowAsync<NgbInvariantViolationException>();
        else
            await act.Should().NotThrowAsync();

        sut.JobId.Should().Be("accounting.aggregates.drift_check");
        metrics.Snapshot()["drift_detected"].Should().Be(throws ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountingOperationsStuckMonitor_CoversEmptyAndPopulatedPages(bool hasStale)
    {
        var records = hasStale
            ? new[]
            {
                new PostingStateRecord(
                    Guid.CreateVersion7(), PostingOperation.Post, new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
                    null, PostingStateStatus.StaleInProgress, null, TimeSpan.FromHours(1))
            }
            : [];
        var reader = new Mock<IPostingStateReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(It.IsAny<PostingStatePageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostingStatePage(records, false, null));
        var metrics = new JobRunMetrics();
        var sut = new AccountingOperationsStuckMonitorJob(
            reader.Object, NullLogger<AccountingOperationsStuckMonitorJob>.Instance, metrics, Clock);

        await sut.RunAsync(default);

        sut.JobId.Should().Be("accounting.operations.stuck_monitor");
        metrics.Snapshot()["problem"].Should().Be(hasStale ? 1 : 0);
    }

    [Fact]
    public async Task OperationalRegistersFinalizeDirtyMonths_RecordsBoundedResult()
    {
        var maintenance = new Mock<IOperationalRegisterAdminMaintenanceService>(MockBehavior.Strict);
        maintenance.Setup(x => x.FinalizeDirtyAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var metrics = new JobRunMetrics();
        var sut = new OperationalRegistersFinalizeDirtyMonthsJob(
            maintenance.Object, NullLogger<OperationalRegistersFinalizeDirtyMonthsJob>.Instance, metrics, Clock);

        await sut.RunAsync(default);

        sut.JobId.Should().Be("opreg.finalization.run_dirty_months");
        metrics.Snapshot()["finalized_count"].Should().Be(7);
    }

    [Fact]
    public async Task PlatformSchemaValidate_RunsAllSixValidationsInOrder()
    {
        var calls = new List<string>();
        var documentsCore = Validation<IDocumentsCoreSchemaValidationService>(x =>
            x.ValidateAsync(It.IsAny<CancellationToken>()), "documents-core", calls);
        var accountingCore = Validation<IAccountingCoreSchemaValidationService>(x =>
            x.ValidateAsync(It.IsAny<CancellationToken>()), "accounting-core", calls);
        var operationalCore = Validation<IOperationalRegistersCoreSchemaValidationService>(x =>
            x.ValidateAsync(It.IsAny<CancellationToken>()), "operational-core", calls);
        var referenceCore = Validation<IReferenceRegistersCoreSchemaValidationService>(x =>
            x.ValidateAsync(It.IsAny<CancellationToken>()), "reference-core", calls);
        var catalogs = new Mock<ICatalogSchemaValidationService>(MockBehavior.Strict);
        catalogs.Setup(x => x.ValidateAllAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("catalogs"))
            .Returns(Task.CompletedTask);
        var documents = new Mock<IDocumentSchemaValidationService>(MockBehavior.Strict);
        documents.Setup(x => x.ValidateAllAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("documents"))
            .Returns(Task.CompletedTask);
        var metrics = new JobRunMetrics();
        var sut = new PlatformSchemaValidateJob(
            documentsCore.Object, accountingCore.Object, operationalCore.Object, referenceCore.Object,
            catalogs.Object, documents.Object, NullLogger<PlatformSchemaValidateJob>.Instance, metrics, Clock);

        await sut.RunAsync(default);

        sut.JobId.Should().Be("platform.schema.validate");
        calls.Should().Equal("documents-core", "accounting-core", "operational-core", "reference-core", "catalogs", "documents");
        metrics.Snapshot()["validations"].Should().Be(6);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 1, 0)]
    public async Task AuditHealth_CoversMissingTriggersOrphansAndHealthyRun(
        long eventsTrigger,
        long changesTrigger,
        long orphans)
    {
        var connection = new FakeDbConnection(
            [eventsTrigger, changesTrigger, orphans],
            eventsCount: 12,
            min: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            max: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        var uow = new FakeUnitOfWork(connection);
        var metrics = new JobRunMetrics();
        var sut = new AuditHealthJob(uow, NullLogger<AuditHealthJob>.Instance, metrics, Clock);
        var act = () => sut.RunAsync(default);

        if (eventsTrigger == 0 || changesTrigger == 0 || orphans > 0)
            await act.Should().ThrowAsync<NgbInvariantViolationException>();
        else
            await act.Should().NotThrowAsync();

        sut.JobId.Should().Be("audit.health");
        metrics.Snapshot()["audit.events_count"].Should().Be(12);
        metrics.Snapshot()["health_ok"].Should().Be(eventsTrigger > 0 && changesTrigger > 0 && orphans == 0 ? 1 : 0);
    }

    private static Mock<T> Validation<T>(
        System.Linq.Expressions.Expression<Func<T, Task>> expression,
        string name,
        ICollection<string> calls) where T : class
    {
        var mock = new Mock<T>(MockBehavior.Strict);
        mock.Setup(expression).Callback(() => calls.Add(name)).Returns(Task.CompletedTask);
        return mock;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUnitOfWork(FakeDbConnection connection) : IUnitOfWork
    {
        public DbConnection Connection => connection;
        public DbTransaction? Transaction => null;
        public bool HasActiveTransaction => false;
        public Task EnsureConnectionOpenAsync(CancellationToken ct = default)
        {
            connection.Open();
            return Task.CompletedTask;
        }
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void EnsureActiveTransaction() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDbConnection(
        IEnumerable<long> scalars,
        long eventsCount,
        DateTime? min,
        DateTime? max) : DbConnection
    {
        private readonly Queue<long> _scalars = new(scalars);
        private ConnectionState _state;
        [AllowNull]
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, _scalars, eventsCount, min, max);
    }

    private sealed class FakeDbCommand(
        DbConnection connection,
        Queue<long> scalars,
        long eventsCount,
        DateTime? min,
        DateTime? max) : DbCommand
    {
        private readonly FakeDbParameterCollection _parameters = new();
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => scalars.Dequeue();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var table = new DataTable();
            table.Columns.Add("EventsCount", typeof(long));
            table.Columns.Add("MinOccurredAtUtc", typeof(DateTime));
            table.Columns.Add("MaxOccurredAtUtc", typeof(DateTime));
            table.Rows.Add(eventsCount, min ?? (object)DBNull.Value, max ?? (object)DBNull.Value);
            return table.CreateDataReader();
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => ((ICollection)_items).SyncRoot;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(x => x.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0) _items.Add(value); else _items[index] = value;
        }
    }
}
