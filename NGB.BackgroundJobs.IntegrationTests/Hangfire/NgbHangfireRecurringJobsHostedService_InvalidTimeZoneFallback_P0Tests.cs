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
public sealed class NgbHangfireRecurringJobsHostedService_InvalidTimeZoneFallback_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task StartAsync_WhenTimeZoneIsInvalid_FallsBackToUtc_AndRegistersJob()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new SelectiveScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [jobId] = new JobSchedule(
                JobId: jobId,
                Cron: "0 6 * * *",
                Enabled: true,
                TimeZoneId: "INVALID/TIMEZONE-ID")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        var recurring = GetRecurringJobs(fixture.JobStorage);

        recurring.Should().ContainSingle(x =>
            x.Id == jobId &&
            x.Cron == "0 6 * * *");

        recurring.Single(x => x.Id == jobId).TimeZoneId.Should().Be("UTC");
    }

    private static List<RecurringJobDto> GetRecurringJobs(JobStorage storage)
    {
        using var conn = storage.GetConnection();
        return conn.GetRecurringJobs().ToList();
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

    private sealed class SelectiveScheduleProvider : IJobScheduleProvider
    {
        private readonly IReadOnlyDictionary<string, JobSchedule?> _schedules;

        public SelectiveScheduleProvider(IReadOnlyDictionary<string, JobSchedule?> schedules)
        {
            _schedules = schedules;
        }

        public JobSchedule? GetSchedule(string jobId)
        {
            return _schedules.TryGetValue(jobId, out var schedule) ? schedule : null;
        }
    }
}
