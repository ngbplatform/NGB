using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.CRM.DependencyInjection;
using NGB.CRM.Runtime.Documents.Validation;
using NGB.CRM.Runtime.Posting;
using NGB.CRM.Runtime.Reporting;
using NGB.CRM.Runtime.Reporting.Datasets;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;

namespace NGB.CRM.Runtime.DependencyInjection;

public static class CrmRuntimeModuleServiceCollectionExtensions
{
    public static IServiceCollection AddCrmRuntimeModule(this IServiceCollection services)
    {
        services.TryAddScoped<ICrmSetupService, CrmSetupService>();
        services.TryAddScoped<ICrmDemoSeedService, CrmDemoSeedService>();

        services.AddDefinitionBoundScoped<IDocumentPostValidator, LeadIntakePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, LeadQualificationPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, LeadConversionPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, OpportunityUpdatePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, QuotePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ActivityLogPostValidator>();

        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, LeadIntakeReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, LeadQualificationReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, LeadConversionReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, OpportunityUpdateReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, QuoteReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, ActivityLogReferenceRegisterPostingHandler>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, CrmPostingDefinitionsContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, CrmCanonicalReportDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDatasetSource, CrmOperationalReportsDatasetSource>());

        return services;
    }
}
