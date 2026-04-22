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
public sealed class NgbHangfireRecurringJobsHostedService_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task StartAsync_SchedulesEnabledJobs_AndRemovesDisabledOrMissing()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        // Precreate a recurring job that should be removed.
        manager.AddOrUpdate<PlatformHangfireJobRunner>(
            recurringJobId: PlatformJobCatalog.AuditHealth,
            methodCall: runner => runner.RunAsync(PlatformJobCatalog.AuditHealth, JobCancellationToken.Null),
            cronExpression: "*/5 * * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 3 * * *",
                Enabled: true,
                TimeZoneId: "UTC"),

            // Explicitly disabled => should be removed.
            [PlatformJobCatalog.AuditHealth] = new JobSchedule(
                JobId: PlatformJobCatalog.AuditHealth,
                Cron: "0 3 * * *",
                Enabled: false,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        var recurring = GetRecurringJobs(fixture.JobStorage);

        recurring.Should().ContainSingle(x =>
            x.Id == PlatformJobCatalog.PlatformSchemaValidate &&
            x.Cron == "0 3 * * *" &&
            string.Equals(x.TimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase));

        recurring.Should().NotContain(x => x.Id == PlatformJobCatalog.AuditHealth);
    }


    [Fact]
    public async Task StartAsync_RemovesRecurringJob_WhenScheduleMissing()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        // Precreate a recurring job that should be removed (missing schedule).
        manager.AddOrUpdate<PlatformHangfireJobRunner>(
            recurringJobId: PlatformJobCatalog.AuditHealth,
            methodCall: runner => runner.RunAsync(PlatformJobCatalog.AuditHealth, JobCancellationToken.Null),
            cronExpression: "*/5 * * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 3 * * *",
                Enabled: true,
                TimeZoneId: "UTC")

            // Intentionally no schedule for AuditHealth.
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        var recurring = GetRecurringJobs(fixture.JobStorage);

        recurring.Should().ContainSingle(x =>
            x.Id == PlatformJobCatalog.PlatformSchemaValidate &&
            x.Cron == "0 3 * * *" &&
            string.Equals(x.TimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase));

        recurring.Should().NotContain(x => x.Id == PlatformJobCatalog.AuditHealth);
    }

    [Fact]
    public async Task StartAsync_UpdatesCron_WhenScheduleChanges()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 1 * * *",
                Enabled: true,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        GetRecurringJobs(fixture.JobStorage)
            .Should().ContainSingle(x => x.Id == PlatformJobCatalog.PlatformSchemaValidate && x.Cron == "0 1 * * *");

        // Change cron and re-run startup registration.
        schedules.Set(
            PlatformJobCatalog.PlatformSchemaValidate,
            new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 2 * * *",
                Enabled: true,
                TimeZoneId: "UTC"));

        await hosted.StartAsync(CancellationToken.None);

        GetRecurringJobs(fixture.JobStorage)
            .Should().ContainSingle(x => x.Id == PlatformJobCatalog.PlatformSchemaValidate && x.Cron == "0 2 * * *");
    }

    [Fact]
    public async Task StartAsync_InvalidTimeZone_FallsBackToUtc()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 4 * * *",
                Enabled: true,
                TimeZoneId: "Invalid/TimeZone")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        GetRecurringJobs(fixture.JobStorage)
            .Should().ContainSingle(x =>
                x.Id == PlatformJobCatalog.PlatformSchemaValidate &&
                x.Cron == "0 4 * * *" &&
                string.Equals(x.TimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartAsync_DoesNotRemove_RecurringJobsNotInCatalog()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        const string externalJobId = "thirdparty.cleanup";

        // Create a recurring job that is NOT part of PlatformJobCatalog. The platform must not touch it.
        manager.AddOrUpdate<PlatformHangfireJobRunner>(
            recurringJobId: externalJobId,
            methodCall: runner => runner.RunAsync(externalJobId, JobCancellationToken.Null),
            cronExpression: "0 0 * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Only schedule a single platform job to keep the assertion surface minimal.
        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 3 * * *",
                Enabled: true,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        var recurring = GetRecurringJobs(fixture.JobStorage);

        recurring.Should().ContainSingle(x =>
            x.Id == PlatformJobCatalog.PlatformSchemaValidate &&
            x.Cron == "0 3 * * *" &&
            string.Equals(x.TimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase));

        recurring.Should().ContainSingle(x =>
            x.Id == externalJobId &&
            x.Cron == "0 0 * * *");
    }

    [Fact]
    public async Task StartAsync_WhenCronIsInvalid_Throws_AndDoesNotCreateAnyRecurringJobs()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "THIS_IS_NOT_A_CRON",
                Enabled: true,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        var act = () => hosted.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();

        GetRecurringJobs(fixture.JobStorage).Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenCronIsEmpty_Throws_AndDoesNotCreateAnyRecurringJobs()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "",
                Enabled: true,
                TimeZoneId: "UTC")
        });

        var hosted = new PlatformHangfireRecurringJobsHostedService(
            manager,
            schedules,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        var act = () => hosted.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();

        GetRecurringJobs(fixture.JobStorage).Should().BeEmpty();
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

    private sealed class MutableScheduleProvider : IJobScheduleProvider
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, JobSchedule?> _schedules;

        public MutableScheduleProvider(Dictionary<string, JobSchedule?> schedules)
        {
            _schedules = schedules;
        }

        public void Set(string jobId, JobSchedule? schedule)
        {
            lock (_gate)
                _schedules[jobId] = schedule;
        }

        public JobSchedule? GetSchedule(string jobId)
        {
            lock (_gate)
                return _schedules.TryGetValue(jobId, out var s) ? s : null;
        }
    }
}
