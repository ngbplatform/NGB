using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NGB.Application.Abstractions.Services;

namespace NGB.Api.WorkCenter;

internal sealed class WorkCenterOutboxHealthCheck(IServiceScopeFactory scopes)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var snapshot = await scope.ServiceProvider
            .GetRequiredService<IWorkCenterOperationalHealthReader>()
            .ReadAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["pendingCount"] = snapshot.PendingDeliveryCount,
            ["failedCount"] = snapshot.FailedDeliveryCount,
            ["oldestPendingAgeSeconds"] = snapshot.OldestPendingAgeSeconds,
            ["openTaskCount"] = snapshot.OpenTaskCount,
            ["overdueTaskCount"] = snapshot.OverdueTaskCount
        };

        if (snapshot.OldestPendingAgeSeconds > TimeSpan.FromMinutes(15).TotalSeconds)
            return HealthCheckResult.Unhealthy("Work Center outbox is more than 15 minutes behind.", data: data);

        return snapshot.FailedDeliveryCount > 0
            ? HealthCheckResult.Degraded("Work Center outbox contains failed deliveries.", data: data)
            : HealthCheckResult.Healthy("Work Center outbox is current.", data);
    }
}
