using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.CRM.Documents;
using NGB.CRM.PostgreSql.Documents;
using NGB.CRM.PostgreSql.Reporting;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Reporting;

namespace NGB.CRM.PostgreSql.DependencyInjection;

public static class CrmPostgresModuleServiceCollectionExtensions
{
    public static IServiceCollection AddCrmPostgresModule(this IServiceCollection services)
    {
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                CrmCodes.Account,
                "cat_crm_account",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                CrmCodes.Contact,
                "cat_crm_contact",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                CrmCodes.Product,
                "cat_crm_product",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                CrmCodes.OpportunityStage,
                "cat_crm_opportunity_stage",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICrmDocumentReaders, CrmDocumentReaders>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostgresReportDatasetSource, CrmOperationalReportsPostgresDatasetSource>());

        return services;
    }
}
