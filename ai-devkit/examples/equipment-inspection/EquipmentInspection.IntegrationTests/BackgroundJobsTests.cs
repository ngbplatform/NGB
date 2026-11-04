using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Infrastructure;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.OperationalRegisters;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class BackgroundJobsTests
{
    private const string FinalizationJob = "opreg.finalization.run_dirty_months";

    [Fact]
    public async Task Scheduled_worker_finalizes_real_movements_and_restart_preserves_registration_and_projections()
    {
        await using var application = new InspectionHost();
        await application.StartAsync();
        using var admin = await application.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Worker bench")));
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("hours", 6.25m)));
        var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + document.Id;
        var draft = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        using var postResponse = await ExecutePostAsync(admin, path, draft!.DocumentVersion, "worker-post");
        var posted = await ReadActionAsync(postResponse);

        var errors = new ExceptionCapture();
        await using (var worker = CreateWorker(application, configureServices: services =>
            services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(errors))))
        {
            using var client = worker.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            using var health = await client.GetAsync("/health");
            await EnsureSuccessAsync(health);
            using var dashboard = await client.GetAsync("/hangfire");
            Assert.True(dashboard.StatusCode == HttpStatusCode.Redirect,
                $"Dashboard returned {(int)dashboard.StatusCode}: {await dashboard.Content.ReadAsStringAsync()} {errors.Exception}");
            Assert.StartsWith(application.IdentityIssuer + "/protocol/openid-connect/auth", dashboard.Headers.Location!.ToString());
            await AssertScheduleAsync(worker.Services);
            await TriggerFinalizationAsync(worker.Services);
            await AssertProjectionAsync(application, 6.25m, movementCount: 1);
            await TriggerFinalizationAsync(worker.Services);
            await AssertProjectionAsync(application, 6.25m, movementCount: 1);
        }

        await using (var restarted = CreateWorker(application))
        {
            using var client = restarted.CreateClient();
            await AssertScheduleAsync(restarted.Services);
            using var unpostResponse = await ExecuteActionAsync(admin, path, "unpost", posted.DocumentVersion, "worker-unpost");
            await ReadActionAsync(unpostResponse);
            await TriggerFinalizationAsync(restarted.Services);
            await AssertProjectionAsync(application, 0m, movementCount: 2);
        }

        await using var disabled = CreateWorker(application, enabled: false);
        using var disabledClient = disabled.CreateClient();
        var report = await disabled.Services.GetRequiredService<IBackgroundJobsHealthReporter>().GetReportAsync(CancellationToken.None);
        Assert.Equal(0, report.DesiredEnabledCount);
        Assert.Equal(0, report.HangfireRegisteredCount);
        Assert.Equal(0, report.MisconfiguredCount);
    }

    [Fact]
    public async Task Failed_job_is_visible_without_automatic_retry_and_explicit_requeue_runs_once()
    {
        await using var application = new InspectionHost();
        await application.StartAsync();
        var probe = new ProbeJob();
        var notifications = new ProblemNotifications();
        await using var worker = CreateWorker(application, configureServices: services =>
        {
            services.AddSingleton<IPlatformBackgroundJob>(probe);
            services.AddSingleton<IBackgroundJobCatalogContributor>(probe);
            services.AddSingleton<IPlatformJobNotifier>(notifications);
        });
        using var client = worker.CreateClient();
        var jobs = worker.Services.GetRequiredService<IBackgroundJobClient>();
        var storage = worker.Services.GetRequiredService<JobStorage>();
        var id = jobs.Enqueue<PlatformHangfireJobRunner>(runner => runner.RunAsync(ProbeJob.Id, JobCancellationToken.Null));
        await WaitForStateAsync(storage, id, "Failed");
        Assert.Equal(1, probe.Attempts);
        var failed = Assert.Single(notifications.Results, result => result.Outcome == PlatformJobRunOutcome.Failed);
        Assert.Equal(ProbeJob.Id, failed.JobId);
        Assert.NotNull(failed.Error);
        Assert.DoesNotContain(storage.GetMonitoringApi().JobDetails(id).History, state => state.StateName == "Scheduled");

        probe.ShouldFail = false;
        Assert.True(jobs.Requeue(id, "Failed"));
        await WaitForStateAsync(storage, id, "Succeeded");
        Assert.Equal(2, probe.Attempts);
        Assert.Equal(1, probe.Completed);
        Assert.Single(notifications.Results);
        var history = storage.GetMonitoringApi().JobDetails(id).History;
        Assert.Contains(history, state => state.StateName == "Failed");
        Assert.Contains(history, state => state.StateName == "Succeeded");
    }

    [Fact]
    public async Task Overlapping_runs_are_skipped_and_cancellation_releases_the_job_lock()
    {
        await using var application = new InspectionHost();
        await application.StartAsync();
        var probe = new BlockingProbe();
        var notifications = new ProblemNotifications();
        await using var worker = CreateWorker(application, enabled: false, configureServices: services =>
        {
            services.AddSingleton<IPlatformBackgroundJob>(probe);
            services.AddSingleton<IPlatformJobNotifier>(notifications);
        });
        using var client = worker.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runner = worker.Services.GetRequiredService<PlatformHangfireJobRunner>();
        var running = runner.RunAsync(BlockingProbe.Id, new TestCancellation(cancellation.Token));
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var concurrent = worker.Services.GetRequiredService<PlatformHangfireJobRunner>();
            await concurrent.RunAsync(BlockingProbe.Id, new TestCancellation(CancellationToken.None));
            Assert.Equal(1, probe.Attempts);
            var overlap = Assert.Single(notifications.Results, result => result.Outcome == PlatformJobRunOutcome.SkippedOverlap);
            Assert.Equal(1, overlap.Counters["skipped_overlap"]);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
        Assert.Single(notifications.Results, result => result.Outcome == PlatformJobRunOutcome.Cancelled);
        probe.Release.SetResult();
        await runner.RunAsync(BlockingProbe.Id, new TestCancellation(CancellationToken.None));
        Assert.Equal(2, probe.Attempts);
        Assert.Equal(2, notifications.Results.Count);
    }

    private static WebApplicationFactory<BackgroundJobs.Program> CreateWorker(
        InspectionHost application, bool enabled = true, Action<IServiceCollection>? configureServices = null)
        => new WebApplicationFactory<BackgroundJobs.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", application.ConnectionString);
            builder.UseSetting("ConnectionStrings:Hangfire", application.ConnectionString);
            builder.UseSetting("KeycloakSettings:Issuer", application.IdentityIssuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("BackgroundJobs:Enabled", enabled.ToString());
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => configureServices?.Invoke(services));
        });

    private static async Task AssertScheduleAsync(IServiceProvider services)
    {
        var report = await services.GetRequiredService<IBackgroundJobsHealthReporter>().GetReportAsync(CancellationToken.None);
        Assert.Equal(1, report.DesiredEnabledCount);
        Assert.Equal(1, report.HangfireRegisteredCount);
        Assert.Equal(0, report.MisconfiguredCount);
        var job = Assert.Single(report.Jobs, row => row.IsHangfireRegistered);
        Assert.Equal(FinalizationJob, job.JobId);
        Assert.Equal("*/5 * * * *", job.DesiredCron);
        Assert.Equal(job.DesiredCron, job.HangfireCron);
        Assert.Equal("UTC", job.DesiredTimeZoneId);
        Assert.Equal("UTC", job.HangfireTimeZoneId);
        Assert.NotNull(job.NextExecutionUtc);
    }

    private static async Task TriggerFinalizationAsync(IServiceProvider services)
    {
        services.GetRequiredService<IRecurringJobManager>().Trigger(FinalizationJob);
        var storage = services.GetRequiredService<JobStorage>();
        string? id;
        using (var connection = storage.GetConnection())
            id = Assert.Single(connection.GetRecurringJobs(), job => job.Id == FinalizationJob).LastJobId;
        Assert.False(string.IsNullOrWhiteSpace(id));
        await WaitForStateAsync(storage, id, "Succeeded");
    }

    private static async Task WaitForStateAsync(JobStorage storage, string id, string expected)
    {
        var watch = Stopwatch.StartNew();
        string? actual = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            using var connection = storage.GetConnection();
            actual = connection.GetStateData(id)?.Name;
            if (actual == expected)
                return;
            Assert.False(actual == "Failed" && expected != "Failed", "The worker failed: " + id);
            await Task.Delay(100);
        }
        Assert.Fail($"Worker did not reach {expected}; job={id}, actual={actual}.");
    }

    private static async Task AssertProjectionAsync(InspectionHost application, decimal expected, int movementCount)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = new DateOnly(today.Year, today.Month, 1);
        var movements = await reads.GetMovementsPageAsync(new OperationalRegisterMovementsPageRequest(register, month, month));
        Assert.Equal(movementCount, movements.Lines.Count);
        var request = new OperationalRegisterMonthlyProjectionPageRequest(register, month, month);
        var turnovers = await reads.GetTurnoversPageAsync(request);
        var balances = await reads.GetBalancesPageAsync(request);
        Assert.Equal(expected, turnovers.Lines.Sum(row => row.Values["hours"]));
        Assert.Equal(expected, balances.Lines.Sum(row => row.Values["hours"]));
    }

    private sealed class ProbeJob : IPlatformBackgroundJob, IBackgroundJobCatalogContributor
    {
        public const string Id = "example.test.failure-probe";
        private int _attempts;
        private int _completed;

        public string JobId => Id;
        public bool ShouldFail { get; set; } = true;
        public int Attempts => Volatile.Read(ref _attempts);
        public int Completed => Volatile.Read(ref _completed);

        public IReadOnlyCollection<string> GetJobIds() => [Id];

        public Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _attempts);
            if (ShouldFail)
                throw new InvalidOperationException("Injected worker failure before completing any work.");

            Interlocked.Increment(ref _completed);
            return Task.CompletedTask;
        }
    }

    private sealed class ProblemNotifications : IPlatformJobNotifier
    {
        public ConcurrentQueue<PlatformJobRunResult> Results { get; } = new();

        public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
        {
            Results.Enqueue(result);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingProbe : IPlatformBackgroundJob
    {
        public const string Id = "example.test.overlap-probe";
        private int _attempts;

        public string JobId => Id;
        public int Attempts => Volatile.Read(ref _attempts);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TestCancellation(CancellationToken token) : IJobCancellationToken
    {
        public CancellationToken ShutdownToken => token;

        public void ThrowIfCancellationRequested() => token.ThrowIfCancellationRequested();
    }

    private sealed class ExceptionCapture : IExceptionHandler
    {
        public Exception? Exception { get; private set; }

        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            Exception = exception;
            return ValueTask.FromResult(false);
        }
    }
}
