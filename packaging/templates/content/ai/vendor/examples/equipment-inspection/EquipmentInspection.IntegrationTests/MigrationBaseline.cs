using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;

namespace EquipmentInspection.IntegrationTests;

// These contracts belong to MigrationFixtures/Baseline, not the current application.
internal sealed class MigrationBaseline : IDefinitionsContributor
{
    public const string EquipmentCode = "example.equipment";
    public const string InspectionCode = "example.inspection";

    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<IDefinitionsContributor, MigrationBaseline>();
        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            EquipmentCode,
            "cat_example_equipment",
            [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));
        services.AddScoped<IDocumentTypeStorage>(provider => new PostgresHeadDocumentTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            InspectionCode,
            "doc_example_inspection",
            []));
    }

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(EquipmentCode, catalog => catalog.Metadata(new CatalogTypeMetadata(
            CatalogCode: EquipmentCode,
            DisplayName: "Equipment",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_example_equipment",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true, MaxLength: 200, UiLabel: "Equipment name")
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_example_equipment", "display"),
            Version: new CatalogMetadataVersion(1, "equipment-inspection"))));

        builder.AddDocument(InspectionCode, document => document.Metadata(new DocumentTypeMetadata(
            TypeCode: InspectionCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_example_inspection",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, MaxLength: 200, UiLabel: "Inspection name"),
                        new("equipment_id", ColumnType.Guid, UiLabel: "Equipment",
                            Lookup: new CatalogLookupSourceMetadata(EquipmentCode)),
                        new("result", ColumnType.String, MaxLength: 2000, UiLabel: "Inspection result")
                    ])
            ])));
    }
}
