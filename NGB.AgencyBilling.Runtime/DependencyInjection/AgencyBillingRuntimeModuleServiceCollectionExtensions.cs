using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.AgencyBilling.Runtime.Catalogs.Validation;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Derivations;
using NGB.AgencyBilling.Runtime.Reporting;
using NGB.AgencyBilling.Runtime.Reporting.Datasets;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.Runtime.Documents.Derivations;

namespace NGB.AgencyBilling.Runtime.DependencyInjection;

public static class AgencyBillingRuntimeModuleServiceCollectionExtensions
{
    public static IServiceCollection AddAgencyBillingRuntimeModule(this IServiceCollection services)
    {
        services.TryAddScoped<IAgencyBillingSetupService, AgencyBillingSetupService>();
        services.TryAddScoped<IAgencyBillingAccountingPolicyReader, AgencyBillingAccountingPolicyReader>();

        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, ProjectCatalogUpsertValidator>();
        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, RateCardCatalogUpsertValidator>();
        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, AccountingPolicyCatalogUpsertValidator>();

        services.AddDefinitionBoundScoped<IDocumentPostValidator, ClientContractPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, TimesheetPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, SalesInvoicePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, CustomerPaymentPostValidator>();

        services.TryAddScoped<GenerateInvoiceDraftFromTimesheetDerivationHandler>();
        services.AddScoped<IDocumentDerivationHandler>(sp =>
            sp.GetRequiredService<GenerateInvoiceDraftFromTimesheetDerivationHandler>());
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, SalesInvoicePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, CustomerPaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, TimesheetOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, SalesInvoiceOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, CustomerPaymentOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, ClientContractReferenceRegisterPostingHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, AgencyBillingCatalogValidationDefinitionsContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, AgencyBillingDerivationDefinitionsContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, AgencyBillingPostingDefinitionsContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, AgencyBillingCanonicalReportDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDatasetSource, AgencyBillingOperationalReportsDatasetSource>());
        return services;
    }
}
