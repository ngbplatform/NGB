using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Reporting;

namespace EquipmentInspection.PostgreSql;

public static class EquipmentPersistence
{
    public static IServiceCollection AddEquipmentPersistence(this IServiceCollection services)
    {
        services.AddScoped<IInspectionReader, InspectionReader>();
        services.AddScoped<IEquipmentUsageReader, EquipmentUsageReader>();
        services.AddScoped<IEquipmentConditionReader, EquipmentConditionReader>();
        services.AddScoped<IEquipmentServiceReader, EquipmentServiceReader>();
        services.AddSingleton<IDefinitionsContributor, ChecklistStorageDefinitions>();
        services.AddScoped<IChecklistReader, ChecklistReader>();
        services.AddScoped<ChecklistStorage>();
        services.AddScoped<IDocumentTypeStorage>(provider => provider.GetRequiredService<ChecklistStorage>());
        services.AddSingleton<IPostgresReportDatasetSource, InspectionReportDataset>();
        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentDefinitions.EquipmentCode,
            "cat_example_equipment",
            [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));
        services.AddScoped<IDocumentTypeStorage>(provider => new PostgresHeadDocumentTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentDefinitions.InspectionCode,
            "doc_example_inspection",
            []));
        services.AddScoped<IDocumentTypeStorage>(provider => new PostgresHeadDocumentTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentUsageDefinitions.DocumentCode,
            "doc_example_equipment_usage",
            []));

        services.AddScoped<IDocumentTypeStorage>(provider => new PostgresHeadDocumentTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentConditionDefinitions.DocumentCode,
            "doc_example_equipment_condition",
            []));

        services.AddScoped<IDocumentTypeStorage>(provider => new PostgresHeadDocumentTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentServiceDefinitions.DocumentCode,
            "doc_example_equipment_service",
            []));

        return services;
    }
}
