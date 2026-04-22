using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.Runtime.Catalogs;
using NGB.PropertyManagement.Runtime.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Posting;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.Reporting;
using NGB.Runtime.Documents.Validation;

namespace NGB.PropertyManagement.Runtime.DependencyInjection;

public static class PropertyManagementRuntimeModuleServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyManagementRuntimeModule(this IServiceCollection services)
    {
        services.TryAddScoped<IPropertyManagementSetupService, PropertyManagementSetupService>();

        services.TryAddScoped<IPropertyManagementAccountingPolicyReader, PropertyManagementAccountingPolicyReader>();
        services.TryAddScoped<IPropertyManagementBankAccountReader, PropertyManagementBankAccountReader>();
        services.TryAddScoped<IPropertyManagementPartyReader, PropertyManagementPartyReader>();

        services.TryAddScoped<IReceivablesOpenItemsService, ReceivablesOpenItemsService>();
        services.TryAddScoped<IReceivablesOpenItemsDetailsService, ReceivablesOpenItemsDetailsService>();
        services.TryAddScoped<IReceivablesFifoApplySuggestService, ReceivablesFifoApplySuggestService>();
        services.TryAddScoped<IReceivablesFifoApplyExecuteService, ReceivablesFifoApplyExecuteService>();
        services.TryAddScoped<IReceivablesCustomApplyExecuteService, ReceivablesCustomApplyExecuteService>();
        services.TryAddScoped<IReceivablesApplyBatchService, ReceivablesApplyBatchService>();
        services.TryAddScoped<IReceivablesUnapplyService, ReceivablesUnapplyService>();
        services.TryAddScoped<IPayablesOpenItemsService, PayablesOpenItemsService>();
        services.TryAddScoped<IPayablesOpenItemsDetailsService, PayablesOpenItemsDetailsService>();
        services.TryAddScoped<IPayablesFifoApplySuggestService, PayablesFifoApplySuggestService>();
        services.TryAddScoped<IPayablesApplyBatchService, PayablesApplyBatchService>();
        services.TryAddScoped<IPayablesUnapplyService, PayablesUnapplyService>();

        // UI-oriented document effects (action availability)
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentUiEffectsContributor, ReceivablesDocumentUiEffectsContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentUiEffectsContributor, PayablesDocumentUiEffectsContributor>());

        // Catalog validators
        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, PartyCatalogUpsertValidator>();
        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, PropertyCatalogUpsertValidator>();
        services.AddDefinitionBoundScoped<ICatalogUpsertValidator, BankAccountCatalogUpsertValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, PropertyManagementCatalogValidationDefinitionsContributor>());

        // Bulk ops for pm.property
        services.TryAddScoped<IPropertyBulkCreateUnitsService, PropertyBulkCreateUnitsService>();

        // PM reporting
        // - canonical definitions/executors are the only PM-specific reporting runtime registrations
        // - PM-specific filter UX for shared accounting reports is provided through definition enrichers on the final reporting stack
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportDimensionScopeExpander, PropertyManagementPropertyDimensionScopeExpander>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, PropertyManagementCanonicalReportDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionEnricher, PropertyManagementAccountingReportDefinitionEnricher>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, BuildingSummaryCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, OccupancySummaryCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, MaintenanceQueueCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, TenantStatementCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, ReceivablesAgingCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, ReceivablesOpenItemsCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, ReceivablesOpenItemsDetailsCanonicalReportExecutor>());

        // Posting handlers
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, LeaseReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, MaintenanceRequestReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, WorkOrderReferenceRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, WorkOrderCompletionReferenceRegisterPostingHandler>();

        // Post validators
        services.AddDefinitionBoundScoped<IDocumentPostValidator, LeaseOverlapPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, MaintenanceRequestPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, WorkOrderPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, WorkOrderCompletionPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, RentChargePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ReceivableChargePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, LateFeeChargePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ReceivablePaymentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ReceivableReturnedPaymentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ReceivableCreditMemoPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, PayableChargePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, PayablePaymentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, PayableCreditMemoPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, ReceivableApplyPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, PayableApplyPostValidator>();

        // Draft-time payload validators
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, LeasePrimaryPartyPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, MaintenanceRequestPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, WorkOrderPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, WorkOrderCompletionPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, LeasePropertyMustBeUnitPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, RentChargePayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, ReceivableChargePayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, LateFeeChargePayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, ReceivablePaymentPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, ReceivableReturnedPaymentPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, ReceivableCreditMemoPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, PayableChargePayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, PayablePaymentPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, PayableCreditMemoPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, ReceivableApplyPayloadValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentDraftPayloadValidator, PayableApplyPayloadValidator>());

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, RentChargePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, RentChargeOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, ReceivableChargePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, ReceivableChargeOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, LateFeeChargePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, LateFeeChargeOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, ReceivablePaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, ReceivablePaymentOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, ReceivableReturnedPaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, ReceivableReturnedPaymentOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, ReceivableCreditMemoPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, ReceivableCreditMemoOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, PayableChargePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, PayableChargeOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, PayablePaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, PayablePaymentOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, PayableCreditMemoPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, PayableCreditMemoOpenItemsOperationalRegisterPostingHandler>();

        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, ReceivableApplyOpenItemsOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, PayableApplyOpenItemsOperationalRegisterPostingHandler>();

        // Bind posting handlers to document definitions at runtime.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, PropertyManagementPostingDefinitionsContributor>());

        return services;
    }
}
