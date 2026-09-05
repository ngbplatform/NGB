using System.Data;
using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresReferencePayloadBatchEnrichmentReaderFullCoverageTests
{
    [Fact]
    public async Task ResolveAsync_UsesOneCommandForAllReferenceKinds_AndAddsStableFallbacks()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var missingAccountId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var registerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var catalogId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var documentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var rows = Rows(
            (1, null, accountId, null, null, "1010 — Cash", 0),
            (2, null, registerId, null, null, "STOCK — Stock", 0),
            (3, "cat.party", catalogId, null, null, "Party", 0),
            (4, null, documentId, "doc.invoice", "INV-1", null, 1),
            (4, null, documentId, "doc.invoice", null, "Invoice display", 0));
        var connection = new RecordingDbConnection(_ => rows.CreateDataReader());
        var catalogs = new CatalogTypeRegistry();
        catalogs.Register(new CatalogTypeMetadata(
            "cat.party",
            "Party",
            [new CatalogTableMetadata("cat_party", TableKind.Head, [], [])],
            new CatalogPresentationMetadata("cat_party", "display"),
            new CatalogMetadataVersion(1, "tests")));
        var documents = new DocumentTypeRegistry([
            new DocumentTypeMetadata(
                "doc.invoice",
                [new DocumentTableMetadata(
                    "doc_invoice",
                    TableKind.Head,
                    [new DocumentColumnMetadata("display", ColumnType.String)])],
                new DocumentPresentationMetadata("Invoice"))
        ]);
        var sut = new PostgresReferencePayloadBatchEnrichmentReader(
            new RecordingUnitOfWork(connection),
            catalogs,
            documents);

        var result = await sut.ResolveAsync(
            [accountId, missingAccountId, accountId, Guid.Empty],
            [registerId],
            new Dictionary<string, IReadOnlyCollection<Guid>>(StringComparer.OrdinalIgnoreCase)
            {
                ["cat.party"] = [catalogId]
            },
            [documentId]);

        connection.Commands.Should().ContainSingle();
        var command = connection.Commands[0];
        command.CommandText.Should().Contain("FROM accounting_accounts");
        command.CommandText.Should().Contain("FROM operational_registers");
        command.CommandText.Should().Contain("FROM \"cat_party\"");
        command.CommandText.Should().Contain("FROM documents");
        command.CommandText.Should().Contain("JOIN \"doc_invoice\"");
        result.AccountLabels.Should().Contain(accountId, "1010 — Cash");
        result.AccountLabels.Should().Contain(missingAccountId, missingAccountId.ToString());
        result.OperationalRegisterLabels.Should().Contain(registerId, "STOCK — Stock");
        result.CatalogLabelsByType["cat.party"].Should().Contain(catalogId, "Party");
        result.DocumentLabels.Should().Contain(documentId, "Invoice display");
    }

    [Fact]
    public async Task ResolveAsync_WhenEveryInputIsEmpty_DoesNotOpenOrQueryDatabase()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresReferencePayloadBatchEnrichmentReader(
            new RecordingUnitOfWork(connection),
            new CatalogTypeRegistry(),
            new DocumentTypeRegistry());

        var result = await sut.ResolveAsync(
            [],
            [],
            new Dictionary<string, IReadOnlyCollection<Guid>>(StringComparer.OrdinalIgnoreCase)
            {
                ["cat.empty"] = []
            },
            []);

        connection.Commands.Should().BeEmpty();
        connection.State.Should().Be(ConnectionState.Closed);
        result.AccountLabels.Should().BeEmpty();
        result.OperationalRegisterLabels.Should().BeEmpty();
        result.CatalogLabelsByType.Should().ContainKey("cat.empty").WhoseValue.Should().BeEmpty();
        result.DocumentLabels.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WithTypedDocumentBatch_GeneratesOnlyRequestedTypeBranches()
    {
        var documentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var rows = Rows(
            (4, null, documentId, "doc.invoice", "INV-2", null, 1),
            (4, null, documentId, "doc.invoice", null, "Invoice display", 0));
        var connection = new RecordingDbConnection(_ => rows.CreateDataReader());
        var documents = new DocumentTypeRegistry([
            DocumentMetadata("doc.invoice", "doc_invoice"),
            DocumentMetadata("doc.unrelated", "doc_unrelated")
        ]);
        var sut = new PostgresReferencePayloadBatchEnrichmentReader(
            new RecordingUnitOfWork(connection),
            new CatalogTypeRegistry(),
            documents);

        var result = await sut.ResolveAsync(
            [],
            [],
            new Dictionary<string, IReadOnlyCollection<Guid>>(),
            new Dictionary<string, IReadOnlyCollection<Guid>>(StringComparer.OrdinalIgnoreCase)
            {
                ["doc.invoice"] = [documentId]
            });

        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("JOIN \"doc_invoice\"")
            .And.NotContain("doc_unrelated");
        result.DocumentLabels.Should().Contain(documentId, "Invoice display");
    }

    [Fact]
    public async Task ResolveAsync_CoversMissingLabelsUntypedDocumentsAndMetadataWithoutDisplayHeads()
    {
        var accountId = Guid.NewGuid();
        var registerId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        var numberedDocumentId = Guid.NewGuid();
        var unnumberedDocumentId = Guid.NewGuid();
        var orphanTypedRowId = Guid.NewGuid();
        var rows = Rows(
            (1, null, accountId, null, null, null, 0),
            (3, "cat.party", catalogId, null, null, null, 0),
            (3, null, Guid.NewGuid(), null, null, "ignored", 0),
            (4, null, numberedDocumentId, "doc.invoice", "INV-3", null, 1),
            (4, null, unnumberedDocumentId, "doc.unknown", null, null, 1),
            (4, null, orphanTypedRowId, "doc.invoice", null, "orphan", 0));
        var connection = new RecordingDbConnection(_ => rows.CreateDataReader());
        var catalogs = new CatalogTypeRegistry();
        catalogs.Register(new CatalogTypeMetadata(
            "cat.party",
            "Party",
            [new CatalogTableMetadata("cat_party", TableKind.Head, [], [])],
            new CatalogPresentationMetadata("cat_party", "display"),
            new CatalogMetadataVersion(1, "tests")));
        var documents = new DocumentTypeRegistry([
            DocumentMetadata("doc.invoice", "doc_invoice"),
            new DocumentTypeMetadata(
                "doc.no-head",
                [new DocumentTableMetadata("doc_no_head", TableKind.Part, [])],
                new DocumentPresentationMetadata("No head")),
            new DocumentTypeMetadata(
                "doc.no-display",
                [new DocumentTableMetadata("doc_no_display", TableKind.Head, [])],
                new DocumentPresentationMetadata("No display"))
        ]);
        var sut = new PostgresReferencePayloadBatchEnrichmentReader(
            new RecordingUnitOfWork(connection), catalogs, documents);

        var result = await sut.ResolveAsync(
            [accountId],
            [registerId],
            new Dictionary<string, IReadOnlyCollection<Guid>> { ["cat.party"] = [catalogId] },
            [numberedDocumentId, unnumberedDocumentId, orphanTypedRowId]);

        result.AccountLabels[accountId].Should().Be(accountId.ToString());
        result.OperationalRegisterLabels[registerId].Should().Be(registerId.ToString());
        result.CatalogLabelsByType["cat.party"][catalogId].Should().BeEmpty();
        result.DocumentLabels[numberedDocumentId].Should().Be("doc.invoice INV-3");
        result.DocumentLabels[unnumberedDocumentId].Should()
            .Be($"doc.unknown {unnumberedDocumentId.ToString("N")[..8]}");
        result.DocumentLabels.Should().NotContainKey(orphanTypedRowId);
        connection.Commands[0].CommandText.Should()
            .NotContain("doc_no_head")
            .And.NotContain("doc_no_display");
    }

    [Fact]
    public async Task ResolveAsync_RejectsEveryNullCollection()
    {
        var sut = new PostgresReferencePayloadBatchEnrichmentReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            new CatalogTypeRegistry(),
            new DocumentTypeRegistry());

        await FluentActions.Invoking(() => sut.ResolveAsync(null!, [], EmptyCatalogs(), Array.Empty<Guid>()))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ResolveAsync([], null!, EmptyCatalogs(), Array.Empty<Guid>()))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ResolveAsync([], [], null!, Array.Empty<Guid>()))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ResolveAsync([], [], EmptyCatalogs(), (IReadOnlyCollection<Guid>)null!))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ResolveAsync([], [], EmptyCatalogs(),
                (IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>)null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private static DocumentTypeMetadata DocumentMetadata(string typeCode, string tableName)
        => new(
            typeCode,
            [new DocumentTableMetadata(
                tableName,
                TableKind.Head,
                [new DocumentColumnMetadata("display", ColumnType.String)])],
            new DocumentPresentationMetadata(typeCode));

    private static Dictionary<string, IReadOnlyCollection<Guid>> EmptyCatalogs() => [];

    private static DataTable Rows(params (short Kind, string? SourceCode, Guid Id, string? TypeCode, string? Number, string? Display, int Priority)[] values)
    {
        var table = new DataTable();
        table.Columns.Add("Kind", typeof(short));
        table.Columns.Add("SourceCode", typeof(string));
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Number", typeof(string));
        table.Columns.Add("Display", typeof(string));
        table.Columns.Add("Priority", typeof(int));
        foreach (var value in values)
        {
            table.Rows.Add(
                value.Kind,
                value.SourceCode ?? (object)DBNull.Value,
                value.Id,
                value.TypeCode ?? (object)DBNull.Value,
                value.Number ?? (object)DBNull.Value,
                value.Display ?? (object)DBNull.Value,
                value.Priority);
        }

        return table;
    }
}
