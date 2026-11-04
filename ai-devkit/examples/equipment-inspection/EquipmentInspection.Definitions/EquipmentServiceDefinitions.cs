using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class EquipmentServiceDefinitions : IDefinitionsContributor
{
    public const string DocumentCode = "example.equipment-service";
    public const string RegisterCode = "example.equipment-service-hours";
    public const string ExpenseAccountCode = "example.service-expense";
    public const string PayableAccountCode = "example.service-payable";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocument(DocumentCode, document => document.Metadata(new DocumentTypeMetadata(
            TypeCode: DocumentCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_example_equipment_service",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, MaxLength: 200, UiLabel: "Service description"),
                        new("equipment_id", ColumnType.Guid, UiLabel: "Equipment",
                            Lookup: new CatalogLookupSourceMetadata(EquipmentDefinitions.EquipmentCode)),
                        new("hours", ColumnType.Decimal, UiLabel: "Service hours"),
                        new("amount", ColumnType.Decimal, UiLabel: "Service cost"),
                        new("condition", ColumnType.String, MaxLength: 200, UiLabel: "Condition after service")
                    ])
            ])));
    }
}
