using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Infrastructure;

namespace NGB.BackgroundJobs.Tests;

public sealed class NgbHangfireRecurringJobsHostedService_P0Tests
{
    [Fact]
    public async Task StartAsync_SchedulesEnabledJob_AndRemovesOthers()
    {
        var manager = new RecordingRecurringJobManager();
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var scheduleProvider = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: true, TimeZoneId: "UTC")
        });

        var svc = new PlatformHangfireRecurringJobsHostedService(
            manager,
            scheduleProvider,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        manager.Added.Should().ContainSingle(x =>
            x.JobId == jobId &&
            x.Cron == "0 2 * * *" &&
            x.TimeZone == TimeZoneInfo.Utc);

        manager.Removed.Should().HaveCount(PlatformJobCatalog.All.Count - 1);
        manager.Removed.Should().NotContain(jobId);
    }

    [Fact]
    public async Task StartAsync_FallsBackToUtc_WhenTimeZoneIsUnknown()
    {
        var manager = new RecordingRecurringJobManager();
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var scheduleProvider = new DictionaryScheduleProvider(new Dictionary<string, JobSchedule>
        {
            [jobId] = new JobSchedule(jobId, Cron: "0 2 * * *", Enabled: true, TimeZoneId: "NotATimeZone")
        });

        var svc = new PlatformHangfireRecurringJobsHostedService(
            manager,
            scheduleProvider,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        manager.Added.Should().ContainSingle(x => x.JobId == jobId);
        manager.Added[0].TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    private sealed record AddCall(string JobId, string Cron, TimeZoneInfo TimeZone);

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<AddCall> Added { get; } = new();
        public List<string> Removed { get; } = new();

        // Hangfire 1.8 exposes AddOrUpdate via RecurringJobOptions. Some older API variants used TimeZoneInfo (+ optional queue).
        // Implement both signatures to keep this unit-test fake resilient across minor package variations.
        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            Added.Add(new AddCall(recurringJobId, cronExpression, options.TimeZone ?? TimeZoneInfo.Utc));
        }

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, TimeZoneInfo timeZone)
        {
            Added.Add(new AddCall(recurringJobId, cronExpression, timeZone));
        }

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, TimeZoneInfo timeZone, string queue)
        {
            Added.Add(new AddCall(recurringJobId, cronExpression, timeZone));
        }

        public void RemoveIfExists(string recurringJobId)
        {
            Removed.Add(recurringJobId);
        }

        public void Trigger(string recurringJobId)
        {
            throw new NotSupportedException("Trigger is not used by these unit tests.");
        }
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
            if (_schedules.TryGetValue(jobId, out var s))
                return s;
            return null;
        }
    }
}
