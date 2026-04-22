using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Documents.Validation;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.DependencyInjection;
using NGB.Trade.Runtime.Documents.Validation;
using NGB.Trade.Runtime.Pricing;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Runtime.Posting;
using NGB.Trade.Runtime.Reporting;
using NGB.Trade.Runtime.Reporting.Datasets;

namespace NGB.Trade.Runtime.DependencyInjection;

public static class TradeRuntimeModuleServiceCollectionExtensions
{
    public static IServiceCollection AddTradeRuntimeModule(this IServiceCollection services)
    {
        services.TryAddScoped<ITradeSetupService, TradeSetupService>();
        services.TryAddScoped<ITradeDemoSeedService, TradeDemoSeedService>();
        services.TryAddScoped<ITradeAccountingPolicyReader, TradeAccountingPolicyReader>();
        services.TryAddScoped<TradeInventoryAvailabilityService>();
        services.TryAddScoped<TradeDocumentLineDefaultsService>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, PurchaseReceiptPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, SalesInvoicePostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, CustomerPaymentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, VendorPaymentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, InventoryTransferPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, InventoryAdjustmentPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, CustomerReturnPostValidator>();
        services.AddDefinitionBoundScoped<IDocumentPostValidator, VendorReturnPostValidator>();

        services.AddDefinitionBoundScoped<IDocumentPostingHandler, PurchaseReceiptPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, PurchaseReceiptInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, SalesInvoicePostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, SalesInvoiceInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, CustomerPaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, VendorPaymentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, InventoryTransferInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, InventoryAdjustmentPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, InventoryAdjustmentInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, CustomerReturnPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, CustomerReturnInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentPostingHandler, VendorReturnPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentOperationalRegisterPostingHandler, VendorReturnInventoryOperationalRegisterPostingHandler>();
        services.AddDefinitionBoundScoped<IDocumentReferenceRegisterPostingHandler, ItemPriceUpdateReferenceRegisterPostingHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, TradePostingDefinitionsContributor>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, TradeCanonicalReportDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDatasetSource, TradeOperationalReportsDatasetSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, TradeDashboardOverviewCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, SalesByItemCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, SalesByCustomerCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, PurchasesByVendorCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, CurrentItemPricesCanonicalReportExecutor>());

        return services;
    }
}
