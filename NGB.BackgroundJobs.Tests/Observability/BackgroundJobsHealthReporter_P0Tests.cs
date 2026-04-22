using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Observability;

namespace NGB.BackgroundJobs.Tests.Observability;

public sealed class BackgroundJobsHealthReporter_P0Tests
{
    [Fact]
    public async Task EnabledJob_RegisteredAndMatchesCronAndTz_IsNotMisconfigured()
    {
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: true, TimeZoneId: "UTC")
        });

        var recurring = new DictionaryRecurringReader(new Dictionary<string, RecurringJobState?>
        {
            [jobId] = new RecurringJobState(jobId, Cron: "0 2 * * *", TimeZoneId: "UTC", LastExecutionUtc: null, NextExecutionUtc: null, LastJobId: null, LastJobState: null, Error: null)
        });

        var reporter = new BackgroundJobsHealthReporter(schedules, recurring, NullLogger<BackgroundJobsHealthReporter>.Instance);

        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.CatalogJobCount.Should().Be(PlatformJobCatalog.All.Count);
        report.DesiredEnabledCount.Should().Be(1);
        report.HangfireRegisteredCount.Should().Be(1);
        report.MisconfiguredCount.Should().Be(0);

        var row = report.Jobs.Single(x => x.JobId == jobId);
        row.DesiredEnabled.Should().BeTrue();
        row.IsHangfireRegistered.Should().BeTrue();
        row.IsMisconfigured.Should().BeFalse();
        row.DesiredCron.Should().Be("0 2 * * *");
        row.HangfireCron.Should().Be("0 2 * * *");
        row.DesiredTimeZoneId.Should().Be("UTC");
        row.HangfireTimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public async Task EnabledJob_WithCronDrift_IsMisconfigured()
    {
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: true, TimeZoneId: "UTC")
        });

        var recurring = new DictionaryRecurringReader(new Dictionary<string, RecurringJobState?>
        {
            [jobId] = new RecurringJobState(jobId, Cron: "0 3 * * *", TimeZoneId: "UTC", LastExecutionUtc: null, NextExecutionUtc: null, LastJobId: null, LastJobState: null, Error: null)
        });

        var reporter = new BackgroundJobsHealthReporter(schedules, recurring, NullLogger<BackgroundJobsHealthReporter>.Instance);

        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.MisconfiguredCount.Should().Be(1);
        report.Jobs.Single(x => x.JobId == jobId).IsMisconfigured.Should().BeTrue();
    }

    [Fact]
    public async Task EnabledJob_NotRegistered_IsMisconfigured()
    {
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: true, TimeZoneId: "UTC")
        });

        var recurring = new DictionaryRecurringReader(new Dictionary<string, RecurringJobState?>());

        var reporter = new BackgroundJobsHealthReporter(schedules, recurring, NullLogger<BackgroundJobsHealthReporter>.Instance);

        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.MisconfiguredCount.Should().Be(1);
        report.Jobs.Single(x => x.JobId == jobId).IsMisconfigured.Should().BeTrue();
    }

    [Fact]
    public async Task DisabledJob_ButRegistered_IsMisconfigured()
    {
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: false, TimeZoneId: "UTC")
        });

        var recurring = new DictionaryRecurringReader(new Dictionary<string, RecurringJobState?>
        {
            [jobId] = new RecurringJobState(jobId, Cron: "0 2 * * *", TimeZoneId: "UTC", LastExecutionUtc: null, NextExecutionUtc: null, LastJobId: null, LastJobState: null, Error: null)
        });

        var reporter = new BackgroundJobsHealthReporter(schedules, recurring, NullLogger<BackgroundJobsHealthReporter>.Instance);

        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.MisconfiguredCount.Should().Be(1);
        report.Jobs.Single(x => x.JobId == jobId).IsMisconfigured.Should().BeTrue();
    }

    private sealed class DictionaryScheduleProvider : IJobScheduleProvider
    {
        private readonly IReadOnlyDictionary<string, JobSchedule> _schedules;

        public DictionaryScheduleProvider(IReadOnlyDictionary<string, JobSchedule> schedules)
        {
            _schedules = schedules;
        }

        public JobSchedule? GetSchedule(string jobId)
        {
            return _schedules.TryGetValue(jobId, out var s) ? s : null;
        }
    }

    private sealed class DictionaryRecurringReader : IRecurringJobStateReader
    {
        private readonly IReadOnlyDictionary<string, RecurringJobState?> _states;

        public DictionaryRecurringReader(IReadOnlyDictionary<string, RecurringJobState?> states)
        {
            _states = states;
        }

        public ValueTask<RecurringJobState?> TryGetAsync(string jobId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_states.TryGetValue(jobId, out var s) ? s : null);
        }
    }
}
