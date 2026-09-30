using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.BackgroundJobs.Jobs;
using NGB.BackgroundJobs.Observability;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Schema;
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
            Mock.Of<IAuditHealthReader>(), NullLogger<AuditHealthJob>.Instance, metrics)
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
        var snapshotScope = new Mock<IAsyncDisposable>(MockBehavior.Strict);
        snapshotScope.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var snapshotScopeFactory = new Mock<IDbSchemaSnapshotScopeFactory>(MockBehavior.Strict);
        snapshotScopeFactory.Setup(x => x.BeginSnapshotScopeAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IAsyncDisposable>(snapshotScope.Object));
        var metrics = new JobRunMetrics();
        var sut = new PlatformSchemaValidateJob(
            documentsCore.Object, accountingCore.Object, operationalCore.Object, referenceCore.Object,
            catalogs.Object, documents.Object, NullLogger<PlatformSchemaValidateJob>.Instance, metrics, Clock,
            snapshotScopeFactory.Object);

        await sut.RunAsync(default);

        sut.JobId.Should().Be("platform.schema.validate");
        calls.Should().Equal("documents-core", "accounting-core", "operational-core", "reference-core", "catalogs", "documents");
        metrics.Snapshot()["validations"].Should().Be(6);
        snapshotScopeFactory.VerifyAll();
        snapshotScope.VerifyAll();
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
        var reader = new Mock<IAuditHealthReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditHealthSnapshot
            {
                EventsTrigger = eventsTrigger,
                ChangesTrigger = changesTrigger,
                OrphanChanges = orphans,
                EventsCount = 12,
                MinOccurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                MaxOccurredAtUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
            });
        var metrics = new JobRunMetrics();
        var sut = new AuditHealthJob(reader.Object, NullLogger<AuditHealthJob>.Instance, metrics, Clock);
        var act = () => sut.RunAsync(default);

        if (eventsTrigger == 0 || changesTrigger == 0 || orphans > 0)
            await act.Should().ThrowAsync<NgbInvariantViolationException>();
        else
            await act.Should().NotThrowAsync();

        sut.JobId.Should().Be("audit.health");
        metrics.Snapshot()["audit.events_count"].Should().Be(12);
        metrics.Snapshot()["health_ok"].Should().Be(eventsTrigger > 0 && changesTrigger > 0 && orphans == 0 ? 1 : 0);
        reader.VerifyAll();
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
}
