using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;

namespace EquipmentInspection.Definitions;

public sealed class ChecklistDefinitions : IDefinitionsContributor
{
    public const string DocumentCode = "example.inspection-checklist";
    public const string ChecksPartCode = "checks";
    public const int TextMaxLength = 200;

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocument(DocumentCode, document => document.Metadata(new DocumentTypeMetadata(
            TypeCode: DocumentCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_example_checklist",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, MaxLength: TextMaxLength, UiLabel: "Checklist name"),
                        new("reviewed", ColumnType.Boolean, UiLabel: "Reviewed")
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_example_checklist_checks",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("line_no", ColumnType.Int32, Required: true, UiLabel: "Line"),
                        new("description", ColumnType.String, MaxLength: TextMaxLength, UiLabel: "Check"),
                        new("passed", ColumnType.Boolean, UiLabel: "Passed")
                    ],
                    PartCode: ChecksPartCode)
            ])));
    }
}
