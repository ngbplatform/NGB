using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.AgencyBilling.Definitions;
using NGB.AgencyBilling.Documents.Numbering;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;

namespace NGB.AgencyBilling.DependencyInjection;

public static class AgencyBillingModuleServiceCollectionExtensions
{
    public static IServiceCollection AddAgencyBillingModule(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, AgencyBillingDefinitionsContributor>());
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, AgencyBillingClientContractNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, AgencyBillingTimesheetNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, AgencyBillingSalesInvoiceNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, AgencyBillingCustomerPaymentNumberingPolicy>();
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
