using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.CRM.Definitions;
using NGB.CRM.Documents.Numbering;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;

namespace NGB.CRM.DependencyInjection;

public static class CrmModuleServiceCollectionExtensions
{
    public static IServiceCollection AddCrmModule(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, CrmDefinitionsContributor>());

        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmLeadIntakeNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmLeadQualificationNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmLeadConversionNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmOpportunityUpdateNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmQuoteNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, CrmActivityLogNumberingPolicy>();

        return services;
    }

    public static IServiceCollection AddDefinitionBoundScoped<TContract, TImplementation>(this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        services.TryAddScoped<TImplementation>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<TContract, TImplementation>());
        return services;
    }
}
