using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.Security;
using NGB.Runtime.WorkCenter;

namespace NGB.Api.WorkCenter;

[Authorize]
public sealed class WorkCenterHub(IPermissionSnapshotProvider snapshots) : Hub
{
    internal const string UserGroupPrefix = "work-center-user:";

    public override async Task OnConnectedAsync()
    {
        var snapshot = await snapshots.GetCurrentAsync(Context.ConnectionAborted);
        if (snapshot is not { UserId: { } userId, IsAuthenticated: true, IsActive: true })
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    internal static string GroupName(Guid userId) => $"{UserGroupPrefix}{userId:D}";
}

internal sealed class SignalRWorkCenterRealtimeNotifier(IHubContext<WorkCenterHub> hub)
    : IWorkCenterRealtimeNotifier
{
    private const int GroupBatchSize = 500;

    public async Task NotifyUsersChangedAsync(long version, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var groups = userIds
            .Where(static x => x != Guid.Empty)
            .Distinct()
            .Select(WorkCenterHub.GroupName)
            .ToArray();

        foreach (var batch in groups.Chunk(GroupBatchSize))
        {
            await hub.Clients.Groups(batch).SendAsync("workCenterChanged", version, ct);
        }
    }
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
        services.Replace(ServiceDescriptor.Singleton<IWorkCenterRealtimeNotifier, SignalRWorkCenterRealtimeNotifier>());
        return services;
    }

    /// <summary>
    /// Registers this API process as the single owner of Work Center outbox projection.
    /// Keep this explicit so enabling SignalR never starts background processing implicitly.
    /// </summary>
    public static IServiceCollection AddNgbWorkCenterOutboxProcessing(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<NgbWorkCenterOptions>(configuration.GetSection(NgbWorkCenterOptions.ConfigurationSection));
            services.Configure<NgbWorkCenterHostingOptions>(configuration.GetSection(NgbWorkCenterOptions.ConfigurationSection));
        }

        services.AddOptions<NgbWorkCenterHostingOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NgbWorkCenterHostingOptions>, NgbWorkCenterHostingOptionsValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkCenterOutboxHostedService>());
        return services;
    }

    public static IEndpointRouteBuilder MapNgbWorkCenterHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<WorkCenterHub>("/hubs/work-center");
        return endpoints;
    }
}
