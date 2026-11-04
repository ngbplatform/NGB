using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class EquipmentUsageDefinitions : IDefinitionsContributor
{
    public const string DocumentCode = "example.equipment-usage";
    public const string RegisterCode = "example.equipment-hours";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocument(DocumentCode, document => document.Metadata(new DocumentTypeMetadata(
            TypeCode: DocumentCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_example_equipment_usage",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, MaxLength: 200, UiLabel: "Usage description"),
                        new("equipment_id", ColumnType.Guid, UiLabel: "Equipment",
                            Lookup: new CatalogLookupSourceMetadata(EquipmentDefinitions.EquipmentCode)),
                        new("hours", ColumnType.Decimal, UiLabel: "Hours used")
                    ])
            ])));
    }
}
