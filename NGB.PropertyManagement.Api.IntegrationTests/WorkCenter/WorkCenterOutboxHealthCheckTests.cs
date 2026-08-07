using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using NGB.Api.WorkCenter;
using NGB.Persistence.Outbox;
using NGB.Persistence.WorkCenter;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

public sealed class WorkCenterOutboxHealthCheckTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_healthy_when_outbox_is_not_configured()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var health = new WorkCenterOutboxHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now));

        var result = await health.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task Returns_healthy_and_reports_operational_counts_for_current_outbox()
    {
        var result = await CheckAsync(
            pending: 3,
            oldest: null,
            failed: 0,
            openTasks: 5,
            overdueTasks: 2);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("current");
        result.Data.Should().Contain(new KeyValuePair<string, object>("pendingCount", 3L));
        result.Data.Should().Contain(new KeyValuePair<string, object>("failedCount", 0L));
        result.Data.Should().Contain(new KeyValuePair<string, object>("oldestPendingAgeSeconds", 0d));
        result.Data.Should().Contain(new KeyValuePair<string, object>("openTaskCount", 5L));
        result.Data.Should().Contain(new KeyValuePair<string, object>("overdueTaskCount", 2L));
    }

    [Fact]
    public async Task Returns_degraded_when_failed_deliveries_exist()
    {
        var result = await CheckAsync(
            pending: 1,
            oldest: Now.AddMinutes(-5).UtcDateTime,
            failed: 2,
            openTasks: 0,
            overdueTasks: 0);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("failed");
        result.Data["oldestPendingAgeSeconds"].Should().Be(300d);
    }

    [Fact]
    public async Task Returns_unhealthy_before_failed_check_when_outbox_is_stale()
    {
        var result = await CheckAsync(
            pending: 2,
            oldest: Now.AddMinutes(-16).UtcDateTime,
            failed: 4,
            openTasks: 1,
            overdueTasks: 1);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("15 minutes behind");
        result.Data["oldestPendingAgeSeconds"].Should().Be(960d);
    }

    [Fact]
    public async Task Clamps_future_event_age_to_zero_in_health_data()
    {
        var result = await CheckAsync(
            pending: 1,
            oldest: Now.AddMinutes(1).UtcDateTime,
            failed: 0,
            openTasks: 0,
            overdueTasks: 0);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["oldestPendingAgeSeconds"].Should().Be(0d);
    }

    private static async Task<HealthCheckResult> CheckAsync(
        long pending,
        DateTime? oldest,
        long failed,
        long openTasks,
        long overdueTasks)
    {
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        outbox
            .Setup(repository => repository.GetHealthAsync("work-center", CancellationToken.None))
            .ReturnsAsync((pending, oldest, failed));
        var reads = new Mock<IWorkCenterReadRepository>(MockBehavior.Strict);
        reads
            .Setup(repository => repository.GetTaskHealthAsync(Now.UtcDateTime, CancellationToken.None))
            .ReturnsAsync(new WorkCenterTaskHealthRecord(openTasks, overdueTasks));
        using var provider = new ServiceCollection()
            .AddSingleton(outbox.Object)
            .AddSingleton(reads.Object)
            .BuildServiceProvider();
        var health = new WorkCenterOutboxHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now));

        var result = await health.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        outbox.VerifyAll();
        reads.VerifyAll();
        return result;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
