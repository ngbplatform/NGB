using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Jobs;
using NGB.BackgroundJobs.Observability;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Jobs;

public sealed class GeneralJournalEntryAutoReversePostingJob_P0Tests
{
    [Fact]
    public async Task RunAsync_UsesCurrentUtcDate_AndPublishesCounters()
    {
        var nowUtc = new DateTimeOffset(2026, 4, 10, 13, 45, 0, TimeSpan.Zero);
        var runner = new RecordingSystemReversalRunner(returnedPosted: 7);
        var metrics = new JobRunMetrics();
        var job = new GeneralJournalEntryAutoReversePostingJob(
            BuildServices(runner),
            NullLogger<GeneralJournalEntryAutoReversePostingJob>.Instance,
            metrics,
            new FrozenTimeProvider(nowUtc));

        await job.RunAsync(CancellationToken.None);

        job.JobId.Should().Be(PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue);
        runner.UtcDate.Should().Be(new DateOnly(2026, 4, 10));
        runner.BatchSize.Should().Be(GeneralJournalEntryAutoReversePostingJob.MaxPostsPerRun);
        runner.PostedBy.Should().Be(GeneralJournalEntryAutoReversePostingJob.PostedBy);

        var snapshot = metrics.Snapshot();
        snapshot["utc_date_yyyymmdd"].Should().Be(20260410);
        snapshot["max_posts_per_run"].Should().Be(GeneralJournalEntryAutoReversePostingJob.MaxPostsPerRun);
        snapshot["posted_count"].Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_WhenRunnerIsMissing_ThrowsConfigurationViolation_AndSetsMetric()
    {
        var metrics = new JobRunMetrics();
        var job = new GeneralJournalEntryAutoReversePostingJob(
            BuildServices(),
            NullLogger<GeneralJournalEntryAutoReversePostingJob>.Instance,
            metrics);

        var act = () => job.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*IGeneralJournalEntrySystemReversalRunner*");

        metrics.Snapshot()["runner_missing"].Should().Be(1);
    }

    private static IServiceProvider BuildServices(IGeneralJournalEntrySystemReversalRunner? runner = null)
    {
        var services = new ServiceCollection();
        if (runner is not null)
            services.AddSingleton(runner);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingSystemReversalRunner(int returnedPosted) : IGeneralJournalEntrySystemReversalRunner
    {
        public DateOnly? UtcDate { get; private set; }
        public int? BatchSize { get; private set; }
        public string? PostedBy { get; private set; }

        public Task<int> PostDueSystemReversalsAsync(
            DateOnly utcDate,
            int batchSize,
            string postedBy = "SYSTEM",
            CancellationToken ct = default)
        {
            UtcDate = utcDate;
            BatchSize = batchSize;
            PostedBy = postedBy;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(returnedPosted);
        }
    }

    private sealed class FrozenTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
