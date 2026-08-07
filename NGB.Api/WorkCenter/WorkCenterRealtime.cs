using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;

namespace NGB.Api.WorkCenter;

[Authorize]
public sealed class WorkCenterHub : Hub;

internal sealed class SignalRWorkCenterRealtimeNotifier(IHubContext<WorkCenterHub> hub)
    : IWorkCenterRealtimeNotifier
{
    public Task NotifyChangedAsync(long version, CancellationToken ct)
        => hub.Clients.All.SendAsync("workCenterChanged", version, ct);
}

public static class WorkCenterRealtimeExtensions
{
    public static IHealthChecksBuilder AddNgbWorkCenterHealth(this IHealthChecksBuilder healthChecks)
    {
        healthChecks.AddCheck<WorkCenterOutboxHealthCheck>("Work Center outbox", tags: ["ready", "work-center"]);
        return healthChecks;
    }

    public static IServiceCollection AddNgbWorkCenterRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IWorkCenterRealtimeNotifier, SignalRWorkCenterRealtimeNotifier>();
        return services;
    }

    public static IEndpointRouteBuilder MapNgbWorkCenterHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<WorkCenterHub>("/hubs/work-center");
        return endpoints;
    }
}
