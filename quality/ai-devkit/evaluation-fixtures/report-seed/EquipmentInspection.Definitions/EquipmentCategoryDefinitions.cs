using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class EquipmentCategoryDefinitions : IDefinitionsContributor
{
    public const string TypeCode = "example.equipment_category";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(TypeCode, catalog => catalog.Metadata(new CatalogTypeMetadata(
            CatalogCode: TypeCode,
            DisplayName: "Equipment categories",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_example_equipment_category",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("code", ColumnType.String, Required: true, MaxLength: 64, UiLabel: "Code"),
                        new("name", ColumnType.String, Required: true, MaxLength: 200, UiLabel: "Name")
                    ],
                    Indexes: [new("ux_example_equipment_category_code", ["code"], Unique: true)])
            ],
            Presentation: new CatalogPresentationMetadata("cat_example_equipment_category", "name"),
            Version: new CatalogMetadataVersion(1, "equipment-category"))));
    }
}
