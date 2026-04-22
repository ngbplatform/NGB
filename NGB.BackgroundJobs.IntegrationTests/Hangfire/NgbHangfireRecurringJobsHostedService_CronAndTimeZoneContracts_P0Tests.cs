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
public sealed class NgbHangfireRecurringJobsHostedService_CronAndTimeZoneContracts_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task StartAsync_WhenEnabledJobHasEmptyCron_Throws_AndDoesNotRegisterRecurringJob()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var jobId = PlatformJobCatalog.PlatformSchemaValidate;

        var schedules = new SelectiveScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [jobId] = new JobSchedule(
                JobId: jobId,
                Cron: string.Empty,
                Enabled: true,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        Func<Task> act = () => hosted.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>("Hangfire must validate cron and fail-fast on misconfiguration");

        GetRecurringJobs(fixture.JobStorage)
            .Should().BeEmpty("no recurring jobs must be stored if the only enabled job has an invalid cron");
    }

    [Fact]
    public async Task StartAsync_WhenTimeZoneIsWhitespace_FallsBackToUtc_AndRegistersJob()
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
                TimeZoneId: "   ")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        var recurring = GetRecurringJobs(fixture.JobStorage);

        recurring.Should().ContainSingle(x =>
            x.Id == jobId &&
            x.Cron == "0 6 * * *" &&
            string.Equals(x.TimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase));
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
        // Prefer the ctor(JobStorage) if available, but keep a safe fallback for different Hangfire builds.
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
