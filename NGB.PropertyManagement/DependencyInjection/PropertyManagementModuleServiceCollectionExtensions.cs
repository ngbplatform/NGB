using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Documents.Numbering;

namespace NGB.PropertyManagement.DependencyInjection;

public static class PropertyManagementModuleServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyManagementModule(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, PropertyManagementDefinitionsContributor>());

        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmRentChargeNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmReceivableChargeNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmLateFeeChargeNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmReceivablePaymentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmReceivableReturnedPaymentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmReceivableCreditMemoNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmPayableChargeNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmPayablePaymentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmPayableCreditMemoNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmPayableApplyNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmReceivableApplyNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmMaintenanceRequestNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmWorkOrderNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, PmWorkOrderCompletionNumberingPolicy>();

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
