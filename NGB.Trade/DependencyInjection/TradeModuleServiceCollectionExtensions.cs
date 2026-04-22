using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Trade.Definitions;
using NGB.Trade.Documents.Numbering;

namespace NGB.Trade.DependencyInjection;

public static class TradeModuleServiceCollectionExtensions
{
    public static IServiceCollection AddTradeModule(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, TradeDefinitionsContributor>());
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TrdItemPriceUpdateNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TrdPurchaseReceiptNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TrdSalesInvoiceNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeCustomerPaymentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeVendorPaymentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeInventoryTransferNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeInventoryAdjustmentNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeCustomerReturnNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TradeVendorReturnNumberingPolicy>();
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
