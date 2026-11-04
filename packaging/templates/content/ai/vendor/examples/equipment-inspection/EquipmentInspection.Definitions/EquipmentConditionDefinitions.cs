using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class EquipmentConditionDefinitions : IDefinitionsContributor
{
    public const string DocumentCode = "example.equipment-condition";
    public const string RegisterCode = "example.equipment-condition-records";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocument(DocumentCode, document => document.Metadata(new DocumentTypeMetadata(
            TypeCode: DocumentCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_example_equipment_condition",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, MaxLength: 200, UiLabel: "Assessment description"),
                        new("equipment_id", ColumnType.Guid, UiLabel: "Equipment",
                            Lookup: new CatalogLookupSourceMetadata(EquipmentDefinitions.EquipmentCode)),
                        new("condition", ColumnType.String, MaxLength: 200, UiLabel: "Equipment condition")
                    ])
            ])));
    }
}
