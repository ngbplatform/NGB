using System.Security.Claims;
using FluentAssertions;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Observability;

namespace NGB.BackgroundJobs.Tests.Infrastructure;

public sealed class BackgroundJobsInfrastructureFullCoverageTests
{
    [Fact]
    public void DashboardAuthorization_ReflectsAuthenticatedAnonymousAndMissingIdentity()
    {
        var filter = new HangfireDashboardAuthorizationFilter();
        Dashboard(filter, new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse();
        Dashboard(filter, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "1")], "test"))).Should().BeTrue();
        Dashboard(filter, new ClaimsPrincipal()).Should().BeFalse();
    }

    [Fact]
    public async Task RecurringStateReader_CoversCancellationMissingEmptyAndEveryDateParsePath()
    {
        var connection = new Mock<IStorageConnection>(MockBehavior.Strict);
        connection.SetupSequence(x => x.GetAllEntriesFromHash(It.IsAny<string>()))
            .Returns((Dictionary<string, string>)null!)
            .Returns([])
            .Returns(new Dictionary<string, string>
            {
                ["Cron"] = "0 2 * * *",
                ["TimeZoneId"] = "UTC",
                ["LastExecution"] = "2026-01-01T00:00:00.0000000Z",
                ["NextExecution"] = "2026-01-02T00:00:00",
                ["LastJobId"] = "123",
                ["LastJobState"] = "Succeeded",
                ["Error"] = "none"
            })
            .Returns(new Dictionary<string, string>
            {
                ["LastExecution"] = "invalid",
                ["NextExecution"] = " "
            });
        connection.Setup(x => x.Dispose());
        var storage = new Mock<JobStorage>(MockBehavior.Strict);
        storage.Setup(x => x.GetConnection()).Returns(connection.Object);
        var sut = new HangfireRecurringJobStateReader(storage.Object);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelAct = async () => await sut.TryGetAsync("cancelled", cancelled.Token);
        await cancelAct.Should().ThrowAsync<OperationCanceledException>();
        (await sut.TryGetAsync("missing", default)).Should().BeNull();
        (await sut.TryGetAsync("empty", default)).Should().BeNull();
        var populated = await sut.TryGetAsync("job", default);
        populated.Should().NotBeNull();
        populated!.LastExecutionUtc.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        populated.NextExecutionUtc.Should().NotBeNull();
        var invalid = await sut.TryGetAsync("invalid", default);
        invalid!.LastExecutionUtc.Should().BeNull();
        invalid.NextExecutionUtc.Should().BeNull();
        connection.Verify(x => x.Dispose(), Times.Exactly(4));
    }

    [Fact]
    public async Task HealthReporter_CoversProviderFailureTimeZoneDriftAndCancellation()
    {
        var schedules = new Mock<IJobScheduleProvider>(MockBehavior.Strict);
        schedules.Setup(x => x.GetSchedule("throws")).Throws(new InvalidOperationException("bad provider"));
        schedules.Setup(x => x.GetSchedule("timezone"))
            .Returns(new JobSchedule("timezone", "cron", true, "America/New_York"));
        var recurring = new StubRecurringReader(new Dictionary<string, RecurringJobState?>
        {
            ["throws"] = null,
            ["timezone"] = new RecurringJobState("timezone", "cron", "UTC", null, null, null, null, null)
        });
        var reporter = new BackgroundJobsHealthReporter(
            schedules.Object, recurring, BackgroundJobCatalog.FromJobIds(["throws", "timezone"]),
            NullLogger<BackgroundJobsHealthReporter>.Instance, TimeProvider.System);

        var report = await reporter.GetReportAsync(default);

        report.MisconfiguredCount.Should().Be(1);
        report.Jobs.Single(x => x.JobId == "timezone").IsMisconfigured.Should().BeTrue();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => reporter.GetReportAsync(cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var publicClockConstructor = new BackgroundJobsHealthReporter(
            schedules.Object, recurring, NullLogger<BackgroundJobsHealthReporter>.Instance, TimeProvider.System);
        publicClockConstructor.Should().NotBeNull();
    }

    private static bool Dashboard(HangfireDashboardAuthorizationFilter filter, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        http.RequestServices = new ServiceCollection().BuildServiceProvider();
        var type = Type.GetType("Hangfire.Dashboard.AspNetCoreDashboardContext, Hangfire.AspNetCore", throwOnError: true)!;
        var constructor = type.GetConstructors().OrderBy(x => x.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType.IsInstanceOfType(http)) return (object?)http;
            if (parameter.ParameterType == typeof(JobStorage)) return Mock.Of<JobStorage>();
            if (parameter.ParameterType == typeof(DashboardOptions)) return new DashboardOptions();
            if (parameter.ParameterType == typeof(IServiceProvider)) return http.RequestServices;
            if (parameter.ParameterType == typeof(PathString)) return PathString.Empty;
            if (parameter.HasDefaultValue) return parameter.DefaultValue;
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }).ToArray();
        return filter.Authorize((DashboardContext)constructor.Invoke(arguments));
    }

    private sealed class StubRecurringReader(IReadOnlyDictionary<string, RecurringJobState?> states)
        : IRecurringJobStateReader
    {
        public ValueTask<RecurringJobState?> TryGetAsync(string jobId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(states.GetValueOrDefault(jobId));
    }
}
