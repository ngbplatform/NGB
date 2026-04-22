using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Observability;

[Collection("hangfire-postgres")]
public sealed class BackgroundJobsHealthReporter_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task GetReportAsync_MarksEnabledButMissingRecurringJob_AsMisconfigured()
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

        using var sp = BuildServiceProvider(schedules);
        var reporter = sp.GetRequiredService<IBackgroundJobsHealthReporter>();

        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.CatalogJobCount.Should().Be(PlatformJobCatalog.All.Count);
        report.DesiredEnabledCount.Should().Be(1);
        report.HangfireRegisteredCount.Should().Be(0);
        report.MisconfiguredCount.Should().Be(1);

        report.Jobs.Should().ContainSingle(x =>
            x.JobId == PlatformJobCatalog.PlatformSchemaValidate &&
            x.DesiredEnabled &&
            !x.IsHangfireRegistered &&
            x.IsMisconfigured);
    }

    [Fact]
    public async Task GetReportAsync_MarksCronMismatch_AsMisconfigured()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        manager.AddOrUpdate<PlatformHangfireJobRunner>(
            recurringJobId: PlatformJobCatalog.PlatformSchemaValidate,
            methodCall: runner => runner.RunAsync(PlatformJobCatalog.PlatformSchemaValidate, JobCancellationToken.Null),
            cronExpression: "0 2 * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>
        {
            [PlatformJobCatalog.PlatformSchemaValidate] = new JobSchedule(
                JobId: PlatformJobCatalog.PlatformSchemaValidate,
                Cron: "0 1 * * *",
                Enabled: true,
                TimeZoneId: "UTC")
        });

        using var sp = BuildServiceProvider(schedules);
        var reporter = sp.GetRequiredService<IBackgroundJobsHealthReporter>();
        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.DesiredEnabledCount.Should().Be(1);
        report.HangfireRegisteredCount.Should().Be(1);
        report.MisconfiguredCount.Should().Be(1);

        report.Jobs.Should().ContainSingle(x =>
            x.JobId == PlatformJobCatalog.PlatformSchemaValidate &&
            x.DesiredEnabled &&
            x.IsHangfireRegistered &&
            x.IsMisconfigured &&
            x.DesiredCron == "0 1 * * *" &&
            x.HangfireCron == "0 2 * * *");
    }

    [Fact]
    public async Task GetReportAsync_MarksRegisteredButDisabled_AsMisconfigured()
    {
        var manager = CreateRecurringJobManager(fixture.JobStorage);
        ClearAllRecurringJobs(manager, fixture.JobStorage);

        manager.AddOrUpdate<PlatformHangfireJobRunner>(
            recurringJobId: PlatformJobCatalog.AuditHealth,
            methodCall: runner => runner.RunAsync(PlatformJobCatalog.AuditHealth, JobCancellationToken.Null),
            cronExpression: "*/5 * * * *",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        var schedules = new MutableScheduleProvider(new Dictionary<string, JobSchedule?>());

        using var sp = BuildServiceProvider(schedules);
        var reporter = sp.GetRequiredService<IBackgroundJobsHealthReporter>();
        var report = await reporter.GetReportAsync(CancellationToken.None);

        report.DesiredEnabledCount.Should().Be(0);
        report.HangfireRegisteredCount.Should().Be(1);
        report.MisconfiguredCount.Should().Be(1);

        report.Jobs.Should().ContainSingle(x =>
            x.JobId == PlatformJobCatalog.AuditHealth &&
            !x.DesiredEnabled &&
            x.IsHangfireRegistered &&
            x.IsMisconfigured);
    }

    private ServiceProvider BuildServiceProvider(IJobScheduleProvider schedules)
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<JobStorage>(fixture.JobStorage);

        // Override NullJobScheduleProvider.
        services.AddSingleton<IJobScheduleProvider>(schedules);

        services.AddPlatformBackgroundJobsHangfire(o =>
        {
            o.ConnectionString = fixture.ConnectionString;
            o.PrepareSchemaIfNecessary = true;
            o.WorkerCount = 1;
            o.DistributedLockTimeoutSeconds = 1;
        });

        return services.BuildServiceProvider();
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

        public JobSchedule? GetSchedule(string jobId)
        {
            lock (_gate)
                return _schedules.TryGetValue(jobId, out var s) ? s : null;
        }
    }
}
