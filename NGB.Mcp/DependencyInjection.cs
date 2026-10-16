using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NGB.Mcp.Tools;

namespace NGB.Mcp;

public static class DependencyInjection
{
    public static IServiceCollection AddNgbMcpReadTools(this IServiceCollection services,
        Action<McpReadOptions>? configure = null)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<McpReadOptions>, McpReadOptionsValidator>());
        services.AddOptions<McpReadOptions>().ValidateOnStart();

        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<McpReadToolRegistry>();
        services.TryAddScoped<McpReadDispatcher>();
        services.TryAddSingleton<IMcpNgbLinkResolver, NoNgbLinkResolver>();
        services.TryAddScoped<MetadataReader>();
        services.TryAddScoped<CapabilitiesTool>();
        services.TryAddScoped<ListMetadataTool>();
        services.TryAddScoped<GetMetadataTool>();
        services.TryAddScoped<SearchTool>();
        services.TryAddScoped<ListRecordsTool>();
        services.TryAddScoped<GetRecordTool>();
        services.TryAddScoped<FlowTool>();
        services.TryAddScoped<EffectsTool>();
        services.TryAddScoped<ListReportsTool>();
        services.TryAddScoped<ReportDefinitionTool>();
        services.TryAddScoped<ExecuteReportTool>();
        services.TryAddScoped<AuditTool>();
        services.TryAddScoped<WorkItemsTool>();
        services.TryAddScoped<ContentSummaryTool>();
        services.TryAddScoped<NotesTool>();
        services.TryAddScoped<AttachmentsTool>();
        services.TryAddScoped<CompatibilitySearchTool>();
        services.TryAddScoped<FetchTool>();

        return services;
    }

    public static IServiceCollection AddNgbMcpContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IMcpReadToolContributor
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMcpReadToolContributor, TContributor>());
        return services;
    }
}
