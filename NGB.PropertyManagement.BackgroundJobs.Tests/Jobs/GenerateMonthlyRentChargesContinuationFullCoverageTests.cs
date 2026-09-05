using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.BackgroundJobs.Jobs;
using NGB.PropertyManagement.BackgroundJobs.Services;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.BackgroundJobs.Tests.Jobs;

public sealed class GenerateMonthlyRentChargesContinuationFullCoverageTests
{
    private static readonly Guid LowLeaseId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid MiddleLeaseId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid HighLeaseId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task Job_EnqueuesContinuationAtCandidateChunkBoundary()
    {
        var asOf = new DateOnly(2026, 8, 21);
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, null, null, 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PmRentChargeGenerationLease(Guid.NewGuid(), new DateOnly(2000, 1, 1), null, 1m, 1)]);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new GenerateMonthlyRentChargesService(
            TransactionalUnitOfWork().Object,
            reader.Object,
            new ResultExecutor(static candidate => new RentChargeCandidateExecutionResult(candidate, false, false, null)),
            NullLogger<GenerateMonthlyRentChargesService>.Instance);
        Job? enqueuedJob = null;
        var backgroundJobs = new Mock<IBackgroundJobClient>(MockBehavior.Strict);
        backgroundJobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback((Job job, IState _) => enqueuedJob = job)
            .Returns("continuation-id");
        var job = new GenerateMonthlyRentChargesJob(
            service,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 23, 59, 59, TimeSpan.Zero)),
            backgroundJobs.Object);

        await job.RunAsync(CancellationToken.None);

        backgroundJobs.Verify(x => x.Create(
            It.IsAny<Job>(), It.Is<IState>(state => state.Name == EnqueuedState.StateName)), Times.Once);
        enqueuedJob.Should().NotBeNull();
        enqueuedJob!.Method.Name.Should().Be(nameof(GenerateMonthlyRentChargesJob.ContinueAsync));
        enqueuedJob.Args[0].Should().Be("2026-08-21");
        enqueuedJob.Args[1].Should().BeNull();
        enqueuedJob.Args[3].Should().BeOfType<string>();
        enqueuedJob.Args[5].Should().BeOfType<string>();
    }

    [Fact]
    public async Task Continue_ParsesRequiredAndOptionalDatesAndStopsWhenNoRowsRemain()
    {
        var asOf = new DateOnly(2026, 8, 21);
        var afterStart = new DateOnly(2026, 1, 2);
        var leaseId = Guid.NewGuid();
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, afterStart, leaseId, 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new GenerateMonthlyRentChargesService(
            TransactionalUnitOfWork().Object,
            reader.Object,
            new ResultExecutor(static candidate => new RentChargeCandidateExecutionResult(candidate, true, false, null)),
            NullLogger<GenerateMonthlyRentChargesService>.Instance);
        var job = new GenerateMonthlyRentChargesJob(service, TimeProvider.System);

        await job.ContinueAsync(
            "2026-08-21",
            "2026-01-02",
            leaseId,
            "2026-07-01",
            Guid.NewGuid(),
            "2026-07-01",
            CancellationToken.None);

        reader.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Continue_AcceptsMissingOptionalDates(string? optionalDate)
    {
        var asOf = new DateOnly(2026, 8, 21);
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, null, null, 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new GenerateMonthlyRentChargesService(
            TransactionalUnitOfWork().Object,
            reader.Object,
            new ResultExecutor(static candidate => new RentChargeCandidateExecutionResult(candidate, true, false, null)),
            NullLogger<GenerateMonthlyRentChargesService>.Instance);
        var job = new GenerateMonthlyRentChargesJob(service, TimeProvider.System);

        await job.ContinueAsync(
            "2026-08-21", optionalDate, null, optionalDate, null, optionalDate, CancellationToken.None);

        reader.VerifyAll();
    }

    [Fact]
    public async Task ScopedExecutor_CoversEmptyAndParallelScopedExecution()
    {
        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var lifecycle = new Mock<IDocumentSystemLifecycleService>(MockBehavior.Strict);
        var createInvocation = 0;
        documents.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.RentCharge, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++createInvocation == 1
                ? Task.FromResult(Document(Guid.NewGuid()))
                : YieldDocumentAsync());
        drafts.Setup(x => x.UpdateDraftAsync(
                It.IsAny<Guid>(), null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        lifecycle.Setup(x => x.PostAsync(
                PropertyManagementCodes.RentCharge, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                Document(id, NGB.Contracts.Metadata.DocumentStatus.Posted));
        var services = new ServiceCollection();
        services.AddTransient(_ => new RentChargeCandidateWorker(
            documents.Object, lifecycle.Object, drafts.Object, NullLogger<RentChargeCandidateWorker>.Instance));
        await using var provider = services.BuildServiceProvider();
        var executor = new ScopedRentChargeCandidateBatchExecutor(provider.GetRequiredService<IServiceScopeFactory>());

        (await executor.ExecuteAsync([], CancellationToken.None)).Should().BeEmpty();
        var candidates = new[]
        {
            Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 1)),
            Candidate(Guid.NewGuid(), new DateOnly(2026, 8, 1))
        };
        var results = await executor.ExecuteAsync(candidates, CancellationToken.None);

        results.Should().HaveCount(2).And.OnlyContain(result => result.Created && result.Error == null);
        results.Select(result => result.Candidate).Should().Equal(candidates);
    }

    [Fact]
    public async Task Service_CursorUsesDueLeaseAndPeriodTieBreakers()
    {
        var asOf = new DateOnly(2026, 1, 31);
        var leases = new[]
        {
            new PmRentChargeGenerationLease(LowLeaseId, new DateOnly(2026, 1, 1), null, 1m, 10),
            new PmRentChargeGenerationLease(MiddleLeaseId, new DateOnly(2026, 1, 1), null, 1m, 10),
            new PmRentChargeGenerationLease(MiddleLeaseId, new DateOnly(2026, 1, 2), null, 1m, 10),
            new PmRentChargeGenerationLease(HighLeaseId, new DateOnly(2026, 1, 1), null, 1m, 10),
            new PmRentChargeGenerationLease(Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 1m, 9),
            new PmRentChargeGenerationLease(Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 1m, 11)
        };
        var reader = Reader(asOf, leases, []);
        var executor = new ResultExecutor(static candidate =>
            new RentChargeCandidateExecutionResult(candidate, true, false, null));
        var service = Service(reader, executor);

        var result = await service.ExecuteChunkAsync(
            asOf,
            new RentChargeGenerationCursor(
                null,
                null,
                new DateOnly(2026, 1, 10),
                MiddleLeaseId,
                new DateOnly(2026, 1, 1)),
            CancellationToken.None);

        result.CreatedCount.Should().Be(3);
        executor.LastCandidates.Should().Contain(candidate =>
            candidate.LeaseId == MiddleLeaseId && candidate.PeriodFromUtc == new DateOnly(2026, 1, 2));
        executor.LastCandidates.Should().Contain(candidate => candidate.LeaseId == HighLeaseId);
        executor.LastCandidates.Should().ContainSingle(candidate => candidate.DueOnUtc == new DateOnly(2026, 1, 11));
    }

    [Fact]
    public async Task Service_StopsInsideExistingCandidateRunAndCarriesExactContinuation()
    {
        var asOf = new DateOnly(2025, 11, 30);
        var lease = new PmRentChargeGenerationLease(
            LowLeaseId, new DateOnly(2005, 1, 1), null, 1m, 1);
        var candidates = MonthlyRentChargePlanner.BuildCandidates(lease, asOf);
        candidates.Should().HaveCount(251);
        var existing = candidates.Select(candidate => new PmRentChargePeriodKey(
            candidate.LeaseId, candidate.PeriodFromUtc, candidate.PeriodToUtc)).ToArray();
        var executor = new ResultExecutor(static candidate =>
            new RentChargeCandidateExecutionResult(candidate, true, false, null));
        var service = Service(Reader(asOf, [lease], existing), executor);

        var result = await service.ExecuteChunkAsync(asOf, null, CancellationToken.None);

        result.CandidateCount.Should().Be(250);
        result.SkippedExistingCount.Should().Be(250);
        result.Continuation.Should().NotBeNull();
        result.Continuation!.AfterCandidatePeriodFromUtc.Should().Be(candidates[249].PeriodFromUtc);
        executor.LastCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Service_StopsAfterFourFullLeasePagesEvenWhenTheyHaveNoCandidates()
    {
        var asOf = new DateOnly(2026, 1, 31);
        var leases = Enumerable.Range(0, 256)
            .Select(_ => new PmRentChargeGenerationLease(Guid.NewGuid(), asOf, null, 0m, 1))
            .ToArray();
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = Service(reader, new ResultExecutor(static candidate =>
            new RentChargeCandidateExecutionResult(candidate, true, false, null)));

        var result = await service.ExecuteChunkAsync(asOf, null, CancellationToken.None);

        result.LeaseCount.Should().Be(1_024);
        result.CandidateCount.Should().Be(0);
        result.Continuation.Should().Be(new RentChargeGenerationCursor(asOf, leases[^1].LeaseId));
        reader.Verify(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
            asOf, It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), 256, It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task Service_ContinuesPastFirstFullPageWhenChunkBudgetsRemain()
    {
        var asOf = new DateOnly(2026, 1, 31);
        var leases = Enumerable.Range(0, 256)
            .Select(_ => new PmRentChargeGenerationLease(Guid.NewGuid(), asOf, null, 0m, 1))
            .ToArray();
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.SetupSequence(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases)
            .ReturnsAsync([]);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = Service(reader, new ResultExecutor(static candidate =>
            new RentChargeCandidateExecutionResult(candidate, true, false, null)));

        var result = await service.ExecuteChunkAsync(asOf, null, CancellationToken.None);

        result.LeaseCount.Should().Be(256);
        result.Continuation.Should().BeNull();
        reader.Verify(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
            asOf, It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), 256, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Service_RetainsOnlyFirstSixteenFailuresInAggregate()
    {
        var asOf = new DateOnly(2026, 5, 31);
        var lease = new PmRentChargeGenerationLease(
            LowLeaseId, new DateOnly(2025, 1, 1), null, 1m, 1);
        var executor = new ResultExecutor(candidate => new RentChargeCandidateExecutionResult(
            candidate, false, false, new InvalidOperationException(candidate.PeriodFromUtc.ToString())));
        var service = Service(Reader(asOf, [lease], []), executor);

        var action = () => service.ExecuteChunkAsync(asOf, null, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<NGB.Tools.Exceptions.NgbUnexpectedException>();
        exception.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().HaveCount(16);
        exception.Which.Context["failedCount"].Should().Be(17);
        exception.Which.Context["retainedFailureCount"].Should().Be(16);
    }

    private static Mock<IUnitOfWork> TransactionalUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return uow;
    }

    private static Mock<IPropertyManagementRentChargeGenerationReader> Reader(
        DateOnly asOf,
        IReadOnlyList<PmRentChargeGenerationLease> leases,
        IReadOnlyList<PmRentChargePeriodKey> existing)
    {
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOf, It.IsAny<DateOnly?>(), It.IsAny<Guid?>(), 256, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leases);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        return reader;
    }

    private static GenerateMonthlyRentChargesService Service(
        Mock<IPropertyManagementRentChargeGenerationReader> reader,
        IRentChargeCandidateBatchExecutor executor)
        => new(
            TransactionalUnitOfWork().Object,
            reader.Object,
            executor,
            NullLogger<GenerateMonthlyRentChargesService>.Instance);

    private static MonthlyRentChargeCandidate Candidate(Guid leaseId, DateOnly month) => new(
        leaseId, month, month.AddMonths(1).AddDays(-1), month, 1m, "Rent");

    private static DocumentDto Document(
        Guid id,
        NGB.Contracts.Metadata.DocumentStatus status = NGB.Contracts.Metadata.DocumentStatus.Draft) =>
        new(id, null, new RecordPayload(), status, false);

    private static async Task<DocumentDto> YieldDocumentAsync()
    {
        await Task.Yield();
        return Document(Guid.NewGuid());
    }

    private sealed class ResultExecutor(Func<MonthlyRentChargeCandidate, RentChargeCandidateExecutionResult> resultFactory)
        : IRentChargeCandidateBatchExecutor
    {
        public IReadOnlyList<MonthlyRentChargeCandidate> LastCandidates { get; private set; } = [];

        public Task<IReadOnlyList<RentChargeCandidateExecutionResult>> ExecuteAsync(
            IReadOnlyList<MonthlyRentChargeCandidate> candidates,
            CancellationToken ct)
        {
            LastCandidates = candidates.ToArray();
            return Task.FromResult<IReadOnlyList<RentChargeCandidateExecutionResult>>(
                candidates.Select(resultFactory).ToArray());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
