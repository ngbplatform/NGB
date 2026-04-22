using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentMetadataRuntimeExtensions_P0Tests
{
    [Fact]
    public void CreateHeadDescriptor_UsesExplicitHeadMetadata()
    {
        var meta = new DocumentTypeMetadata(
            TypeCode: "it.doc.meta",
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "it_doc_meta_head",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                        new DocumentColumnMetadata("memo", ColumnType.String)
                    ]),
                new DocumentTableMetadata(
                    TableName: "it_doc_meta__storage_rows",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                    ],
                    PartCode: "lines")
            ],
            Presentation: new DocumentPresentationMetadata("Test"));

        var descriptor = meta.CreateHeadDescriptor();

        descriptor.TypeCode.Should().Be("it.doc.meta");
        descriptor.HeadTableName.Should().Be("it_doc_meta_head");
        descriptor.DisplayColumn.Should().Be("display");
        descriptor.Columns.Select(x => x.ColumnName).Should().Equal("display", "memo");
    }

    [Fact]
    public void GetRequiredPartTable_UsesExplicitPartCode_NotTableNameSuffix()
    {
        var meta = new DocumentTypeMetadata(
            TypeCode: "it.doc.meta",
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "it_doc_meta_head",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                    ]),
                new DocumentTableMetadata(
                    TableName: "it_doc_meta__storage_rows",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                    ],
                    PartCode: "lines")
            ],
            Presentation: new DocumentPresentationMetadata("Test"));

        var partTable = meta.GetRequiredPartTable("lines");

        partTable.TableName.Should().Be("it_doc_meta__storage_rows");
        partTable.PartCode.Should().Be("lines");
    }

    [Fact]
    public void GetRequiredPartTable_WhenPartCodeMissing_ThrowsConfigurationViolation()
    {
        var meta = new DocumentTypeMetadata(
            TypeCode: "it.doc.meta",
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "it_doc_meta_head",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                    ]),
                new DocumentTableMetadata(
                    TableName: "it_doc_meta__storage_rows",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Test"));

        var act = () => meta.GetRequiredPartTable("lines");

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must declare a non-empty PartCode*");
    }
}
