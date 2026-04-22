using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Reporting;
using NGB.Trade.Documents;
using NGB.Trade.Pricing;
using NGB.Trade.PostgreSql.Documents;
using NGB.Trade.PostgreSql.Pricing;
using NGB.Trade.PostgreSql.Reporting;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.DependencyInjection;

public static class TradePostgresModuleServiceCollectionExtensions
{
    public static IServiceCollection AddTradePostgresModule(this IServiceCollection services)
    {
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.Party,
                "cat_trd_party",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.Item,
                "cat_trd_item",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.Warehouse,
                "cat_trd_warehouse",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.UnitOfMeasure,
                "cat_trd_unit_of_measure",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.PaymentTerms,
                "cat_trd_payment_terms",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.InventoryAdjustmentReason,
                "cat_trd_inventory_adjustment_reason",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.PriceType,
                "cat_trd_price_type",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                TradeCodes.AccountingPolicy,
                "cat_trd_accounting_policy",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ITradeDocumentReaders, TradeDocumentReaders>();
        services.AddScoped<ITradePricingLookupReader, TradePricingLookupReader>();
        services.AddScoped<ITradeAnalyticsReader, PostgresTradeAnalyticsReader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostgresReportDatasetSource, TradeOperationalReportsPostgresDatasetSource>());

        return services;
    }
}
