using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class NgbHangfireRecurringJobsHostedService_InvocationData_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task StartAsync_WhenAllJobsEnabled_StoresRunnerInvocationDataForEachJob()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        // Enable all jobs using the same schedule to keep the assertion surface minimal.
        var schedules = new AllEnabledScheduleProvider(cron: "0 5 * * *", timeZoneId: "UTC");

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        using var conn = fixture.JobStorage.GetConnection();

        foreach (var jobId in PlatformJobCatalog.All)
        {
            var hash = conn.GetAllEntriesFromHash($"recurring-job:{jobId}");

            hash.Should().NotBeNull($"a recurring job hash must exist for JobId='{jobId}'");
            hash!.Should().ContainKey("Cron");
            hash.Should().ContainKey("TimeZoneId");
            hash.Should().ContainKey("Job");

            hash["Cron"].Should().Be("0 5 * * *");
            hash["TimeZoneId"].Should().Be("UTC");

            // Golden-ish contract: recurring invocation must call PlatformHangfireJobRunner.RunAsync(jobId, ...)
            // so that platform jobs are always dispatched through a single hardened runner.
            hash["Job"].Should().Contain(nameof(PlatformHangfireJobRunner));
            hash["Job"].Should().Contain("RunAsync");
            hash["Job"].Should().Contain(jobId);
        }
    }

    private static void ClearAllRecurringJobs(IRecurringJobManager manager, JobStorage storage)
    {
        using var conn = storage.GetConnection();
        foreach (var job in conn.GetRecurringJobs())
            manager.RemoveIfExists(job.Id);
    }

    private static IRecurringJobManager CreateRecurringJobManager(JobStorage storage)
    {
        var ctor = typeof(RecurringJobManager).GetConstructor(new[] { typeof(JobStorage) });
        if (ctor is not null)
            return (IRecurringJobManager)ctor.Invoke(new object[] { storage });

        JobStorage.Current = storage;
        return new RecurringJobManager();
    }

    private sealed class AllEnabledScheduleProvider : IJobScheduleProvider
    {
        private readonly string _cron;
        private readonly string _timeZoneId;

        public AllEnabledScheduleProvider(string cron, string timeZoneId)
        {
            _cron = cron;
            _timeZoneId = timeZoneId;
        }

        public JobSchedule? GetSchedule(string jobId) => new(
            JobId: jobId,
            Cron: _cron,
            Enabled: true,
            TimeZoneId: _timeZoneId);
    }
}
