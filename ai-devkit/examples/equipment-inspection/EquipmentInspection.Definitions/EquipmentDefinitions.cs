using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class EquipmentDefinitions : IDefinitionsContributor
{
    public const string EquipmentCode = "example.equipment";
    public const string InspectionCode = "example.inspection";
    public static readonly Guid EquipmentDimensionId = Guid.Parse("08abcdd4-dd60-5e95-a39e-1b79dc835510");

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
                        new("display", ColumnType.String, Required: true, MaxLength: 200, UiLabel: "Equipment name"),
                        new("equipment_code", ColumnType.String, MaxLength: 40, UiLabel: "Equipment code")
                    ],
                    Indexes: [new("ix_example_equipment_code", ["equipment_code"])])
            ],
            Presentation: new CatalogPresentationMetadata("cat_example_equipment", "display"),
            Version: new CatalogMetadataVersion(2, "equipment-inspection-code"))));

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
                        new("source_inspection_id", ColumnType.Guid, UiLabel: "Source inspection",
                            Lookup: new DocumentLookupSourceMetadata([InspectionCode]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("based_on")),
                        new("result", ColumnType.String, MaxLength: 2000, UiLabel: "Inspection result")
                    ])
            ])));
    }
}
