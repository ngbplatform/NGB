using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Derivations;
using NGB.AgencyBilling.PostgreSql.Documents;
using NGB.AgencyBilling.PostgreSql.Derivations;
using NGB.AgencyBilling.PostgreSql.References;
using NGB.AgencyBilling.PostgreSql.Reporting;
using NGB.AgencyBilling.References;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Reporting;

namespace NGB.AgencyBilling.PostgreSql.DependencyInjection;

public static class AgencyBillingPostgresModuleServiceCollectionExtensions
{
    public static IServiceCollection AddAgencyBillingPostgresModule(this IServiceCollection services)
    {
        services.AddScoped<IAgencyBillingDocumentReaders, AgencyBillingDocumentReaders>();
        services.AddScoped<IAgencyBillingReferenceReaders, AgencyBillingReferenceReaders>();
        services.AddScoped<IAgencyBillingInvoiceUsageReader, AgencyBillingInvoiceUsageReader>();
        services.AddScoped<IAgencyBillingInvoiceDraftDerivationReader, AgencyBillingInvoiceDraftDerivationReader>();

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.Client,
                "cat_ab_client",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.TeamMember,
                "cat_ab_team_member",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.Project,
                "cat_ab_project",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.RateCard,
                "cat_ab_rate_card",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.ServiceItem,
                "cat_ab_service_item",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.PaymentTerms,
                "cat_ab_payment_terms",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                AgencyBillingCodes.AccountingPolicy,
                "cat_ab_accounting_policy",
                [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddSingleton<IPostgresReportDatasetSource, AgencyBillingOperationalReportsPostgresDatasetSource>();

        return services;
    }
}
