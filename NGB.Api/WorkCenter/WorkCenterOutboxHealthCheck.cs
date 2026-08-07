using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NGB.Persistence.Outbox;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Observability;

namespace NGB.Api.WorkCenter;

internal sealed class WorkCenterOutboxHealthCheck(IServiceScopeFactory scopes, TimeProvider timeProvider)
    : IHealthCheck
{
    private const string ConsumerCode = "work-center";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetService<IOutboxEventRepository>();
        if (outbox is null)
            return HealthCheckResult.Healthy("Work Center outbox is not configured for this host.");

        var (pending, oldest, failed) = await outbox.GetHealthAsync(ConsumerCode, cancellationToken);
        var taskHealth = await scope.ServiceProvider
            .GetRequiredService<IWorkCenterReadRepository>()
            .GetTaskHealthAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        var age = oldest is null ? TimeSpan.Zero : timeProvider.GetUtcNow().UtcDateTime - oldest.Value;

        NgbFeatureTelemetry.ObserveOperationalHealth(
            pending,
            age.TotalSeconds,
            taskHealth.OpenTaskCount,
            taskHealth.OverdueTaskCount);

        var data = new Dictionary<string, object>
        {
            ["pendingCount"] = pending,
            ["failedCount"] = failed,
            ["oldestPendingAgeSeconds"] = Math.Max(0, age.TotalSeconds),
            ["openTaskCount"] = taskHealth.OpenTaskCount,
            ["overdueTaskCount"] = taskHealth.OverdueTaskCount
        };

        if (age > TimeSpan.FromMinutes(15))
            return HealthCheckResult.Unhealthy("Work Center outbox is more than 15 minutes behind.", data: data);

        return failed > 0
            ? HealthCheckResult.Degraded("Work Center outbox contains failed deliveries.", data: data)
            : HealthCheckResult.Healthy("Work Center outbox is current.", data);
    }
}
