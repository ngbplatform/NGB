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
    public void CreateHeadDescriptor_WhenMetadataIsNull_ThrowsArgumentRequired()
    {
        var act = () => DocumentMetadataRuntimeExtensions.CreateHeadDescriptor(null!);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void CreateHeadDescriptor_WhenHeadTableIsMissing_ThrowsConfigurationViolation()
    {
        var meta = Metadata(
            new DocumentTableMetadata("it_doc_meta__lines", TableKind.Part, [], PartCode: "lines"));

        var act = () => meta.CreateHeadDescriptor();

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*has no head table*");
    }

    [Fact]
    public void CreateHeadDescriptor_WhenDisplayColumnIsMissing_ThrowsConfigurationViolation()
    {
        var meta = Metadata(
            new DocumentTableMetadata(
                "it_doc_meta_head",
                TableKind.Head,
                [new DocumentColumnMetadata("memo", ColumnType.String)]));

        var act = () => meta.CreateHeadDescriptor();

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must define a display column*");
    }

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

    [Fact]
    public void GetRequiredPartTable_WhenMetadataIsNull_ThrowsArgumentRequired()
    {
        var act = () => DocumentMetadataRuntimeExtensions.GetRequiredPartTable(null!, "lines");

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRequiredPartTable_WhenRequestedCodeIsBlank_ThrowsArgumentRequired(string? partCode)
    {
        var act = () => Metadata().GetRequiredPartTable(partCode!);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void GetRequiredPartTable_WhenRequestedPartDoesNotExist_ThrowsConfigurationViolation()
    {
        var meta = Metadata(
            new DocumentTableMetadata("it_doc_meta_head", TableKind.Head, []),
            new DocumentTableMetadata("it_doc_meta__items", TableKind.Part, [], PartCode: "items"));

        var act = () => meta.GetRequiredPartTable(" lines ");

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*does not define part 'lines'*");
    }

    private static DocumentTypeMetadata Metadata(params DocumentTableMetadata[] tables)
        => new(
            TypeCode: "it.doc.meta",
            Tables: tables,
            Presentation: new DocumentPresentationMetadata("Test"));
}
