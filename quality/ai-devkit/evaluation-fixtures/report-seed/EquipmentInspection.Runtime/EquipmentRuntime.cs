using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents.Actions;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.Definitions.WorkCenter;
using NGB.Runtime.Documents.Validation;

namespace EquipmentInspection.Runtime;

public static class EquipmentRuntime
{
    public static IServiceCollection AddEquipmentRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IDefinitionsContributor, EquipmentDefinitions>();
        services.AddSingleton<IMainMenuContributor, EquipmentMainMenuContributor>();
        services.AddSingleton<IDefinitionsContributor, EquipmentCategoryDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentCategoryRuntimeDefinitions>();
        services.AddScoped<EquipmentCategoryValidator>();
        services.AddScoped<ICatalogUpsertValidator>(services =>
            services.GetRequiredService<EquipmentCategoryValidator>());
        services.AddSingleton<IDefinitionsContributor, EquipmentCatalogRuntimeDefinitions>();
        services.AddScoped<EquipmentNameValidator>();
        services.AddScoped<ICatalogUpsertValidator>(services =>
            services.GetRequiredService<EquipmentNameValidator>());
        services.AddSingleton<IDefinitionsContributor, InspectionRuntimeDefinitions>();
        services.AddSingleton<IDefinitionsContributor, InspectionDerivationDefinitions>();
        services.AddSingleton<InspectionNumberingPolicy>();
        services.AddSingleton<IDocumentNumberingPolicy>(services =>
            services.GetRequiredService<InspectionNumberingPolicy>());
        services.AddSingleton<IDefinitionsContributor, InspectionWorkflowDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentUsageDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentUsageRuntimeDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentConditionDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentConditionRuntimeDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentServiceDefinitions>();
        services.AddSingleton<IDefinitionsContributor, EquipmentServiceRuntimeDefinitions>();
        services.AddSingleton<IDefinitionsContributor, ChecklistDefinitions>();
        services.AddSingleton<IDefinitionsContributor, ChecklistRuntimeDefinitions>();
        services.AddScoped<ChecklistDraftValidator>();
        services.AddScoped<IDocumentDraftValidator>(services =>
            services.GetRequiredService<ChecklistDraftValidator>());
        services.AddScoped<IDocumentDraftPayloadValidator, ChecklistPayloadValidator>();
        services.AddScoped<ChecklistApprovalPolicy>();
        services.AddScoped<IDocumentApprovalPolicy>(services =>
            services.GetRequiredService<ChecklistApprovalPolicy>());
        services.AddScoped<ChecklistPostValidator>();
        services.AddScoped<IDocumentPostValidator>(services =>
            services.GetRequiredService<ChecklistPostValidator>());
        services.AddScoped<ChecklistWorkflowPostingHandler>();
        services.AddScoped<IDocumentReferenceRegisterPostingHandler>(services =>
            services.GetRequiredService<ChecklistWorkflowPostingHandler>());
        services.AddScoped<InspectionWorkflowPostingHandler>();
        services.AddScoped<IDocumentReferenceRegisterPostingHandler>(services =>
            services.GetRequiredService<InspectionWorkflowPostingHandler>());
        services.AddScoped<InspectionPostValidator>();
        services.AddScoped<IDocumentPostValidator>(services =>
            services.GetRequiredService<InspectionPostValidator>());
        services.AddScoped<EquipmentUsagePostValidator>();
        services.AddScoped<IDocumentPostValidator>(services =>
            services.GetRequiredService<EquipmentUsagePostValidator>());
        services.AddScoped<EquipmentUsagePostingHandler>();
        services.AddScoped<IDocumentOperationalRegisterPostingHandler>(services =>
            services.GetRequiredService<EquipmentUsagePostingHandler>());
        services.AddScoped<EquipmentConditionPostValidator>();
        services.AddScoped<IDocumentPostValidator>(services =>
            services.GetRequiredService<EquipmentConditionPostValidator>());
        services.AddScoped<EquipmentConditionPostingHandler>();
        services.AddScoped<IDocumentReferenceRegisterPostingHandler>(services =>
            services.GetRequiredService<EquipmentConditionPostingHandler>());
        services.AddScoped<EquipmentServicePostValidator>();
        services.AddScoped<IDocumentPostValidator>(services =>
            services.GetRequiredService<EquipmentServicePostValidator>());
        services.AddScoped<EquipmentServicePostingHandler>();
        services.AddScoped<IDocumentPostingHandler>(services =>
            services.GetRequiredService<EquipmentServicePostingHandler>());
        services.AddScoped<IDocumentOperationalRegisterPostingHandler>(services =>
            services.GetRequiredService<EquipmentServicePostingHandler>());
        services.AddScoped<IDocumentReferenceRegisterPostingHandler>(services =>
            services.GetRequiredService<EquipmentServicePostingHandler>());
        services.AddSingleton<IReportDefinitionSource, InspectionReportDefinitions>();
        services.AddSingleton<IReportDatasetSource, InspectionReportDefinitions>();
        services.AddSingleton<IReportDefinitionSource, InspectionOutcomeReportDefinition>();
        services.AddScoped<IReportSpecializedPlanExecutor, InspectionOutcomeReportExecutor>();
        services.AddSingleton<IWorkCenterPreferenceDefinitionSource, InspectionWorkCenterDefinitions>();
        services.AddScoped<IDocumentActionCompletedWorkCenterPolicy, InspectionWithdrawalPolicy>();

        services.AddSingleton<IDocumentActionDefinitionsContributor, InspectionDeferralDefinitions>();
        services.AddSingleton<InspectionDeferralAvailability>();
        services.AddSingleton<InspectionDeferralAuthorization>();
        services.AddScoped<InspectionDeferralHandler>();
        services.AddScoped<IDocumentActionContextEnricher, InspectionActionContext>();

        return services;
    }
}
