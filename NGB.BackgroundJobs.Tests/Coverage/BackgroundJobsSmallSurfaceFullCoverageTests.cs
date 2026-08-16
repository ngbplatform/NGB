using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Configuration;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Observability;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Coverage;

public sealed class BackgroundJobsSmallSurfaceFullCoverageTests
{
    [Fact]
    public void Catalog_CoversGuardsNullContributorsWhitespaceAndInlineContributor()
    {
        var nullCtor = () => new BackgroundJobCatalog(null!);
        nullCtor.Should().Throw<NgbArgumentRequiredException>();
        var nullInline = () => BackgroundJobCatalog.FromJobIds(null!);
        nullInline.Should().Throw<NgbArgumentRequiredException>();
        var blank = () => new BackgroundJobCatalog([new Contributor(" ")]);
        blank.Should().Throw<NgbConfigurationViolationException>();

        IBackgroundJobCatalogContributor? missing = null;
        var catalog = new BackgroundJobCatalog([missing!, new Contributor(" job.one ")]);
        catalog.All.Should().Equal("job.one");
        BackgroundJobCatalog.FromJobIds(["inline.one"]).All.Should().Equal("inline.one");
        new PlatformBackgroundJobCatalogContributor().GetJobIds().Should().Equal(PlatformJobCatalog.All);
    }

    [Fact]
    public void ScheduleProvider_CoversBlankDisabledFallbackAndTimeZoneBranches()
    {
        var options = new BackgroundJobsSchedulesOptions
        {
            NightlyCron = null,
            DefaultTimeZoneId = "America/New_York",
            NightlyExcludedJobIds = []
        };
        var provider = Provider(options);
        provider.GetSchedule(" ").Should().BeNull();
        provider.GetSchedule("missing").Should().BeNull();

        options.NightlyCron = " 0 1 * * * ";
        options.Jobs["fallback"] = new JobScheduleOptions { Cron = " ", TimeZoneId = " " };
        options.Jobs["override"] = new JobScheduleOptions { Cron = " */5 * * * * ", TimeZoneId = "UTC" };
        provider.GetSchedule("fallback").Should().BeEquivalentTo(
            new JobSchedule("fallback", "0 1 * * *", true, "America/New_York"));
        provider.GetSchedule("override").Should().BeEquivalentTo(
            new JobSchedule("override", "*/5 * * * *", true, "UTC"));

        options.NightlyCron = " ";
        provider.GetSchedule("fallback").Should().BeNull();
    }

    [Fact]
    public void SchedulingRegistration_BindsConfiguredProviderAndReturnsSameCollection()
    {
        var values = new Dictionary<string, string?>
        {
            ["Jobs:Enabled"] = "true",
            ["Jobs:NightlyCron"] = "0 3 * * *"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddPlatformBackgroundJobSchedulesFromConfiguration(configuration, "Jobs").Should().BeSameAs(services);

        using var provider = services.AddLogging().BuildServiceProvider();
        provider.GetRequiredService<IJobScheduleProvider>().Should().BeOfType<ConfigurationJobScheduleProvider>();
        provider.GetRequiredService<IOptions<BackgroundJobsSchedulesOptions>>().Value.NightlyCron.Should().Be("0 3 * * *");
        new NullJobScheduleProvider().GetSchedule("anything").Should().BeNull();
    }

    [Theory]
    [InlineData(PlatformJobRunOutcome.Failed, 0, 0, true)]
    [InlineData(PlatformJobRunOutcome.Succeeded, 1, 0, true)]
    [InlineData(PlatformJobRunOutcome.Succeeded, 0, 2, true)]
    [InlineData(PlatformJobRunOutcome.Succeeded, 0, 0, false)]
    public void Contracts_ExerciseAllMembersAndProblemRules(
        PlatformJobRunOutcome outcome,
        long problem,
        long problemCount,
        bool expected)
    {
        var counters = new Dictionary<string, long>();
        if (problem >= 0) counters["problem"] = problem;
        if (problemCount >= 0) counters["problem_count"] = problemCount;
        var error = outcome == PlatformJobRunOutcome.Failed ? new InvalidOperationException("failed") : null;
        var result = new PlatformJobRunResult(
            "job", "run", outcome, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1), 1000, counters, error);

        result.HasProblems.Should().Be(expected);
        var (jobId, runId, actualOutcome, started, finished, duration, actualCounters, actualError) = result;
        (jobId, runId, actualOutcome, started, finished, duration, actualCounters, actualError)
            .Should().Be(("job", "run", outcome, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1), 1000L, counters, error));

        var row = new BackgroundJobHealthRow(
            "job", true, "cron", "UTC", true, "cron", "UTC", DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddDays(1), "Succeeded", null, false);
        var (_, _, _, _, _, _, _, _, _, _, _, _) = row;
        row.JobId.Should().Be("job");
        var report = new BackgroundJobsHealthReport(DateTime.UnixEpoch, 1, 1, 1, 0, [row]);
        var (_, catalogCount, desiredCount, registeredCount, badCount, jobs) = report;
        (catalogCount, desiredCount, registeredCount, badCount, jobs).Should().Be((1, 1, 1, 0, report.Jobs));

        var recurring = new RecurringJobState(
            "job", "cron", "UTC", DateTime.UnixEpoch, DateTime.UnixEpoch, "id", "Succeeded", "none");
        var (_, _, _, _, _, _, _, _) = recurring;
        recurring.JobId.Should().Be("job");
    }

    [Fact]
    public void HostingOptions_CoversNormalizationDefaultsAndAllValidationFailures()
    {
        var options = new BackgroundJobsHostingOptions
        {
            HealthPath = "health/",
            DashboardPath = "/",
            DashboardTitle = " Title ",
            DashboardBrandSubtitle = " Subtitle ",
            BackgroundJobsSectionName = " Jobs ",
            ApplicationConnectionStringName = " App ",
            HangfireConnectionStringName = " Hangfire ",
            PostgresHealthCheckName = " DB ",
            HangfireHealthCheckName = " Jobs health ",
            AdminConsoleCallbackPath = " ",
            AdminConsolePublicOrigin = " ",
            ServerName = " server "
        };
        options.Queues.Clear();
        options.Queues.Add(" ");
        options.DashboardStylesheetPaths.Clear();
        options.DashboardStylesheetPaths.Add(" css/site.css ");
        options.AddCustomStylesheet(" extra.css ").Should().BeSameAs(options);

        options.ValidateAndNormalize();

        options.HealthPath.Should().Be("/health");
        options.DashboardPath.Should().Be("/");
        options.Queues.Should().Equal("default");
        options.DashboardStylesheetPaths.Should().Equal("css/site.css", "extra.css");
        options.AdminConsoleCallbackPath.Should().BeNull();
        options.AdminConsolePublicOrigin.Should().BeNull();
        options.ServerName.Should().Be("server");

        Action blankStylesheet = () => options.AddCustomStylesheet(" ");
        blankStylesheet.Should().Throw<NgbArgumentRequiredException>();
        InvalidOptions(x => x.HealthPath = " ").Should().Throw<NgbArgumentRequiredException>();
        InvalidOptions(x => x.DashboardTitle = " ").Should().Throw<NgbArgumentRequiredException>();
        InvalidOptions(x => x.WorkerCount = 0).Should().Throw<NgbArgumentOutOfRangeException>();
        InvalidOptions(x => x.DistributedLockTimeoutSeconds = 0).Should().Throw<NgbArgumentOutOfRangeException>();
        InvalidOptions(x => x.AdminConsolePublicOrigin = "ftp://invalid.test").Should().Throw<NgbConfigurationViolationException>();
        InvalidOptions(x => x.AdminConsolePublicOrigin = "not-a-uri").Should().Throw<NgbConfigurationViolationException>();

        var https = new BackgroundJobsHostingOptions { AdminConsolePublicOrigin = "https://example.test/" };
        https.ValidateAndNormalize();
        https.AdminConsolePublicOrigin.Should().Be("https://example.test");
        var http = new BackgroundJobsHostingOptions { AdminConsolePublicOrigin = "http://example.test" };
        http.ValidateAndNormalize();
    }

    [Fact]
    public void PlatformHangfireOptions_ExerciseAllProperties()
    {
        var options = new PlatformHangfireOptions
        {
            ConnectionString = "connection",
            PrepareSchemaIfNecessary = false,
            WorkerCount = 2,
            Queues = ["critical"],
            DistributedLockTimeoutSeconds = 9,
            ServerName = "server"
        };

        (options.ConnectionString, options.PrepareSchemaIfNecessary, options.WorkerCount, options.Queues[0],
            options.DistributedLockTimeoutSeconds, options.ServerName)
            .Should().Be(("connection", false, 2, "critical", 9, "server"));
    }

    [Fact]
    public async Task HostedService_StopAsyncAndExplicitDisabledScheduleAreNoOps()
    {
        var manager = new Mock<global::Hangfire.IRecurringJobManager>(MockBehavior.Strict);
        manager.Setup(x => x.RemoveIfExists("disabled"));
        var schedules = new Mock<IJobScheduleProvider>(MockBehavior.Strict);
        schedules.Setup(x => x.GetSchedule("disabled")).Returns(new JobSchedule("disabled", "cron", false));
        var catalog = BackgroundJobCatalog.FromJobIds(["disabled"]);
        var sut = new PlatformHangfireRecurringJobsHostedService(
            manager.Object, schedules.Object, catalog,
            NullLogger<PlatformHangfireRecurringJobsHostedService>.Instance);

        await sut.StartAsync(default);
        await sut.StopAsync(default);
        manager.VerifyAll();
    }

    private static ConfigurationJobScheduleProvider Provider(BackgroundJobsSchedulesOptions options) =>
        new(Options.Create(options));

    private static Action InvalidOptions(Action<BackgroundJobsHostingOptions> configure) => () =>
    {
        var options = new BackgroundJobsHostingOptions();
        configure(options);
        options.ValidateAndNormalize();
    };

    private sealed class Contributor(params string[] ids) : IBackgroundJobCatalogContributor
    {
        public IReadOnlyCollection<string> GetJobIds() => ids;
    }
}
