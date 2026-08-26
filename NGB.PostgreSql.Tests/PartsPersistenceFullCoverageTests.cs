using FluentAssertions;
using System.Data;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class PartsPersistenceFullCoverageTests
{
    [Fact]
    public async Task Readers_validate_required_arguments_before_opening_a_connection()
    {
        var catalog = new PostgresCatalogPartsReader(null!);
        var document = new PostgresDocumentPartsReader(null!);

        await AssertRequired(() => catalog.GetPartsAsync(null!, Guid.NewGuid()), "partTables");
        await AssertRequired(() => catalog.GetPartsAsync([], Guid.Empty), "catalogId");
        await AssertRequired(() => document.GetPartsAsync(null!, Guid.NewGuid()), "partTables");
        await AssertRequired(() => document.GetPartsAsync([], Guid.Empty), "documentId");
        (await catalog.GetPartsAsync([], Guid.NewGuid())).Should().BeEmpty();
        (await document.GetPartsAsync([], Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task Readers_skip_null_head_and_technical_only_metadata_and_reject_blank_identifiers()
    {
        var catalogConnection = new RecordingDbConnection();
        var catalog = new PostgresCatalogPartsReader(new RecordingUnitOfWork(catalogConnection));
        var catalogResult = await catalog.GetPartsAsync(
            [
                null!,
                CatalogTable("head", TableKind.Head, CatalogColumn("value")),
                CatalogTable("technical", TableKind.Part,
                    CatalogColumn("catalog_id"), CatalogColumn("payload", ColumnType.Json))
            ],
            Guid.NewGuid());
        catalogResult.Should().ContainKey("technical").WhoseValue.Should().BeEmpty();
        catalogConnection.Commands.Should().BeEmpty();

        var documentConnection = new RecordingDbConnection();
        var document = new PostgresDocumentPartsReader(new RecordingUnitOfWork(documentConnection));
        var documentResult = await document.GetPartsAsync(
            [
                null!,
                DocumentTable("head", TableKind.Head, DocumentColumn("value")),
                DocumentTable("technical", TableKind.Part,
                    DocumentColumn("document_id"), DocumentColumn("payload", ColumnType.Json))
            ],
            Guid.NewGuid());
        documentResult.Should().ContainKey("technical").WhoseValue.Should().BeEmpty();
        documentConnection.Commands.Should().BeEmpty();

        await AssertRequired(
            () => catalog.GetPartsAsync([CatalogTable(" ", TableKind.Part, CatalogColumn("value"))], Guid.NewGuid()),
            "TableName");
        await AssertRequired(
            () => document.GetPartsAsync([DocumentTable(" ", TableKind.Part, DocumentColumn("value"))], Guid.NewGuid()),
            "TableName");
        await AssertInvalid(
            () => catalog.GetPartsAsync([CatalogTable("part", TableKind.Part, CatalogColumn(""))], Guid.NewGuid()));
        await AssertInvalid(
            () => document.GetPartsAsync([DocumentTable("part", TableKind.Part, DocumentColumn(""))], Guid.NewGuid()));
    }

    [Theory]
    [InlineData("ordinal", "ORDER BY p.\"ordinal\"")]
    [InlineData("line_no", "ORDER BY p.\"line_no\"")]
    [InlineData("entry_no", "ORDER BY p.\"entry_no\"")]
    [InlineData("id", "ORDER BY p.\"id\"")]
    [InlineData("value", "")]
    public async Task Readers_apply_each_supported_ordering_heuristic(string column, string expectedOrderBy)
    {
        var emptyRows = new DataTable();
        emptyRows.Columns.Add(column, typeof(object));
        var catalogConnection = new RecordingDbConnection(_ => emptyRows.CreateDataReader());
        var catalog = new PostgresCatalogPartsReader(new RecordingUnitOfWork(catalogConnection));
        await catalog.GetPartsAsync(
            [CatalogTable("catalog_part", TableKind.Part, CatalogColumn(column))],
            Guid.NewGuid());
        catalogConnection.Commands.Should().ContainSingle();
        if (expectedOrderBy.Length == 0)
            catalogConnection.Commands[0].CommandText.Should().NotContain("ORDER BY");
        else
            catalogConnection.Commands[0].CommandText.Should().Contain(expectedOrderBy);

        var documentConnection = new RecordingDbConnection(_ => emptyRows.CreateDataReader());
        var document = new PostgresDocumentPartsReader(new RecordingUnitOfWork(documentConnection));
        await document.GetPartsAsync(
            [DocumentTable("document_part", TableKind.Part, DocumentColumn(column))],
            Guid.NewGuid());
        documentConnection.Commands.Should().ContainSingle();
        if (expectedOrderBy.Length == 0)
            documentConnection.Commands[0].CommandText.Should().NotContain("ORDER BY");
        else
            documentConnection.Commands[0].CommandText.Should().Contain(expectedOrderBy);
    }

    [Fact]
    public async Task Readers_materialize_rows_as_case_insensitive_dictionaries()
    {
        var catalogRows = new DataTable();
        catalogRows.Columns.Add("value", typeof(string));
        catalogRows.Rows.Add("catalog-value");
        var catalog = new PostgresCatalogPartsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => catalogRows.CreateDataReader())));
        var catalogResult = await catalog.GetPartsAsync(
            [CatalogTable("catalog_part", TableKind.Part, CatalogColumn("value"))],
            Guid.NewGuid());
        catalogResult["catalog_part"].Should().ContainSingle()
            .Which.Should().Contain("VALUE", "catalog-value");

        var documentRows = new DataTable();
        documentRows.Columns.Add("value", typeof(string));
        documentRows.Rows.Add("document-value");
        var document = new PostgresDocumentPartsReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => documentRows.CreateDataReader())));
        var documentResult = await document.GetPartsAsync(
            [DocumentTable("document_part", TableKind.Part, DocumentColumn("value"))],
            Guid.NewGuid());
        documentResult["document_part"].Should().ContainSingle()
            .Which.Should().Contain("VALUE", "document-value");
    }

    [Fact]
    public async Task Writers_validate_required_arguments_and_empty_metadata_without_a_transaction()
    {
        var catalog = new PostgresCatalogPartsWriter(null!);
        var document = new PostgresDocumentPartsWriter(null!);
        var catalogRows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>();
        var documentRows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>();

        await AssertRequired(() => catalog.ReplacePartsAsync([], Guid.Empty, catalogRows), "catalogId");
        await AssertRequired(() => catalog.ReplacePartsAsync(null!, Guid.NewGuid(), catalogRows), "partTables");
        await AssertRequired(() => catalog.ReplacePartsAsync([], Guid.NewGuid(), null!), "rowsByTable");
        await catalog.ReplacePartsAsync([], Guid.NewGuid(), catalogRows);

        await AssertRequired(() => document.ReplacePartsAsync([], Guid.Empty, documentRows), "documentId");
        await AssertRequired(() => document.ReplacePartsAsync(null!, Guid.NewGuid(), documentRows), "partTables");
        await AssertRequired(() => document.ReplacePartsAsync([], Guid.NewGuid(), null!), "rowsByTable");
        await document.ReplacePartsAsync([], Guid.NewGuid(), documentRows);
    }

    [Fact]
    public async Task Catalog_writer_covers_skip_empty_invalid_and_successful_rows()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresCatalogPartsWriter(new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        var id = Guid.NewGuid();
        var part = CatalogTable("catalog_part", TableKind.Part, CatalogColumn("value"));

        await sut.ReplacePartsAsync([CatalogTable("head", TableKind.Head, CatalogColumn("value"))], id, EmptyRows());
        connection.Commands.Should().BeEmpty();
        await AssertInvalid(() => sut.ReplacePartsAsync(
            [CatalogTable(" ", TableKind.Part, CatalogColumn("value"))], id, EmptyRows()));

        await sut.ReplacePartsAsync([part], id, EmptyRows());
        await sut.ReplacePartsAsync([part], id, Rows(("catalog_part", null!)));

        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("catalog_part", [null!]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("catalog_part", [Row(("catalog_id", id))]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("catalog_part", [Row(("unknown", 1))]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("catalog_part", [Row()]))));
        await AssertInvalid(() => sut.ReplacePartsAsync(
            [CatalogTable("catalog_part", TableKind.Part, CatalogColumn(""))],
            id,
            Rows(("catalog_part", [Row(("", 1))]))));

        await sut.ReplacePartsAsync(
            [part],
            id,
            Rows(("catalog_part", [Row(("value", 1)), Row()])));
        await sut.ReplacePartsAsync(
            [CatalogTable(
                "catalog_part_filtered",
                TableKind.Part,
                CatalogColumn("catalog_id"),
                CatalogColumn("payload", ColumnType.Json),
                CatalogColumn("value"))],
            id,
            Rows(("catalog_part_filtered", [Row(("value", 2))])));
        connection.Commands.Should().Contain(x => x.CommandText.Contains("INSERT INTO \"catalog_part\""));

        var largeRows = Enumerable.Range(0, 501)
            .Select(index => Row(("value", index)))
            .ToArray();
        var beforeLargeWrite = connection.Commands.Count;
        await sut.ReplacePartsAsync([part], id, Rows(("catalog_part", largeRows)));
        connection.Commands.Skip(beforeLargeWrite)
            .Count(command => command.CommandText.Contains("INSERT INTO \"catalog_part\"", StringComparison.Ordinal))
            .Should().Be(2);
    }

    [Fact]
    public async Task Document_writer_covers_skip_empty_invalid_and_successful_rows()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresDocumentPartsWriter(new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        var id = Guid.NewGuid();
        var part = DocumentTable("document_part", TableKind.Part, DocumentColumn("value"));

        await sut.ReplacePartsAsync([DocumentTable("head", TableKind.Head, DocumentColumn("value"))], id, EmptyRows());
        connection.Commands.Should().BeEmpty();
        await AssertInvalid(() => sut.ReplacePartsAsync(
            [DocumentTable(" ", TableKind.Part, DocumentColumn("value"))], id, EmptyRows()));

        await sut.ReplacePartsAsync([part], id, EmptyRows());
        await sut.ReplacePartsAsync([part], id, Rows(("document_part", null!)));

        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("document_part", [null!]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("document_part", [Row(("document_id", id))]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("document_part", [Row(("unknown", 1))]))));
        await AssertInvalid(() => sut.ReplacePartsAsync([part], id, Rows(("document_part", [Row()]))));
        await AssertInvalid(() => sut.ReplacePartsAsync(
            [DocumentTable("document_part", TableKind.Part, DocumentColumn(""))],
            id,
            Rows(("document_part", [Row(("", 1))]))));

        await sut.ReplacePartsAsync(
            [part],
            id,
            Rows(("document_part", [Row(("value", 1)), Row()])));
        await sut.ReplacePartsAsync(
            [DocumentTable(
                "document_part_filtered",
                TableKind.Part,
                DocumentColumn("document_id"),
                DocumentColumn("payload", ColumnType.Json),
                DocumentColumn("value"))],
            id,
            Rows(("document_part_filtered", [Row(("value", 2))])));
        connection.Commands.Should().Contain(x => x.CommandText.Contains("INSERT INTO \"document_part\""));

        var largeRows = Enumerable.Range(0, 501)
            .Select(index => Row(("value", index)))
            .ToArray();
        var beforeLargeWrite = connection.Commands.Count;
        await sut.ReplacePartsAsync([part], id, Rows(("document_part", largeRows)));
        connection.Commands.Skip(beforeLargeWrite)
            .Count(command => command.CommandText.Contains("INSERT INTO \"document_part\"", StringComparison.Ordinal))
            .Should().Be(2);
    }

    private static CatalogTableMetadata CatalogTable(
        string name,
        TableKind kind,
        params CatalogColumnMetadata[] columns)
        => new(name, kind, columns, []);

    private static CatalogColumnMetadata CatalogColumn(string name, ColumnType type = ColumnType.String)
        => new(name, type);

    private static DocumentTableMetadata DocumentTable(
        string name,
        TableKind kind,
        params DocumentColumnMetadata[] columns)
        => new(name, kind, columns);

    private static DocumentColumnMetadata DocumentColumn(string name, ColumnType type = ColumnType.String)
        => new(name, type);

    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] values)
        => values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyRows()
        => new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Rows(
        params (string Table, IReadOnlyList<IReadOnlyDictionary<string, object?>> Values)[] rows)
        => rows.ToDictionary(x => x.Table, x => x.Values, StringComparer.OrdinalIgnoreCase);

    private static async Task AssertRequired(Func<Task> action, string paramName)
        => (await action.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be(paramName);

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<NgbArgumentInvalidException>();
}
