using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentSchemaValidationFullCoverageTests
{
    [Fact]
    public async Task ValidateAll_ReportsEveryTableColumnForeignKeyAndIndexMismatch()
    {
        var metadata = Meta([
            Table("missing-table"),
            Table("no-columns"),
            Table("missing-id", [Column("name", required: true)]),
            Table("nullable-id"),
            Table("no-index-snapshot", indexes: [new DocumentIndexMetadata("missing-index-snapshot", ["document_id"])]),
            Table("bad-values",
                [
                    Column("required_bad", required: true, maxLength: 10),
                    Column("max_unknown", maxLength: 10),
                    Column("max_ok", maxLength: 10),
                    Column("no_max")
                ],
                [
                    new DocumentIndexMetadata("missing", ["required_bad"]),
                    new DocumentIndexMetadata("unique", ["required_bad"], Unique: true),
                    new DocumentIndexMetadata("length", ["required_bad", "max_ok"]),
                    new DocumentIndexMetadata("value", ["required_bad"]),
                    new DocumentIndexMetadata("okay", ["REQUIRED_BAD"], Unique: true),
                    new DocumentIndexMetadata("empty", [])
                ])
        ]);
        var snapshot = Snapshot(
            tables: new HashSet<string>(["no-columns", "missing-id", "nullable-id", "no-index-snapshot", "bad-values"]),
            columns: new Dictionary<string, IReadOnlyList<DbColumnSchema>>
            {
                ["missing-id"] = [],
                ["nullable-id"] = [DbColumn("nullable-id", "document_id", nullable: true)],
                ["no-index-snapshot"] = [DbColumn("no-index-snapshot", "document_id")],
                ["bad-values"] =
                [
                    DbColumn("bad-values", "document_id"),
                    DbColumn("bad-values", "document_id"),
                    DbColumn("bad-values", "required_bad", type: "bad", nullable: true, maxLength: 5),
                    DbColumn("bad-values", "max_unknown", maxLength: null),
                    DbColumn("bad-values", "max_ok", maxLength: 10),
                    DbColumn("bad-values", "no_max")
                ]
            },
            foreignKeys: new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>
            {
                ["nullable-id"] = [new("nullable-id", "fk-wrong", "other", "documents", "id")],
                ["bad-values"] = [new("bad-values", "fk", "DOCUMENT_ID", "DOCUMENTS", "ID")]
            },
            indexes: new Dictionary<string, IReadOnlyList<DbIndexSchema>>
            {
                ["bad-values"] =
                [
                    new("bad-values", "unique", ["required_bad"], false),
                    new("bad-values", "length", ["required_bad"], false),
                    new("bad-values", "value", ["other"], false),
                    new("bad-values", "okay", ["required_bad"], true),
                    new("bad-values", "empty", [], false),
                    new("bad-values", "okay", ["ignored-duplicate"], false)
                ]
            });

        var error = await ((Func<Task>)(() => Service([metadata], snapshot).ValidateAllAsync(default)))
            .Should().ThrowAsync<DocumentSchemaValidationException>();

        error.Which.Message.Should().Contain("missing table")
            .And.Contain("cannot read columns")
            .And.Contain("must have 'document_id'")
            .And.Contain("must have FK")
            .And.Contain("missing column 'name'")
            .And.Contain("must be NOT NULL")
            .And.Contain("has type 'bad'")
            .And.Contain("max length is 5")
            .And.Contain("missing index")
            .And.Contain("must be UNIQUE")
            .And.Contain("columns mismatch");
    }

    [Fact]
    public async Task ValidateAll_AllowsEmptyRegistryAndHealthyCaseInsensitiveSchema()
    {
        await Service([], Snapshot(new HashSet<string>())).ValidateAllAsync(default);

        var healthy = Meta([
            new DocumentTableMetadata("no-index-metadata", TableKind.Head, [], Indexes: null),
            Table("healthy",
                [Column("name", required: true, maxLength: 10)],
                [new DocumentIndexMetadata("ix", ["document_id", "name"], Unique: true)])
        ]);
        var snapshot = Snapshot(
            new HashSet<string>(["no-index-metadata", "healthy"]),
            new Dictionary<string, IReadOnlyList<DbColumnSchema>>
            {
                ["no-index-metadata"] = [DbColumn("no-index-metadata", "document_id")],
                ["healthy"] =
                [
                    DbColumn("healthy", "document_id"),
                    DbColumn("healthy", "name", nullable: false, maxLength: 20)
                ]
            },
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>
            {
                ["no-index-metadata"] = [new("no-index-metadata", "fk", "document_id", "documents", "id")],
                ["healthy"] = [new("healthy", "fk", "DOCUMENT_ID", "DOCUMENTS", "ID")]
            },
            new Dictionary<string, IReadOnlyList<DbIndexSchema>>
            {
                ["healthy"] = [new("healthy", "ix", ["DOCUMENT_ID", "NAME"], true)]
            });

        await Service([healthy], snapshot).ValidateAllAsync(default);
    }

    private static DocumentSchemaValidationService Service(
        IReadOnlyCollection<DocumentTypeMetadata> metadata,
        DbSchemaSnapshot snapshot)
    {
        var registry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.GetAll()).Returns(metadata);
        var schema = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        schema.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var mapper = new Mock<IDbTypeMapper>(MockBehavior.Strict);
        mapper.Setup(x => x.IsCompatible(It.IsAny<ColumnType>(), It.IsAny<string>()))
            .Returns<ColumnType, string>((_, dbType) => dbType != "bad");
        mapper.Setup(x => x.GetExpectedDbType(It.IsAny<ColumnType>())).Returns("text");
        return new DocumentSchemaValidationService(
            registry.Object, schema.Object, mapper.Object, NullLogger<DocumentSchemaValidationService>.Instance);
    }

    private static DocumentTypeMetadata Meta(IReadOnlyList<DocumentTableMetadata> tables)
        => new("doc", tables);

    private static DocumentTableMetadata Table(
        string name,
        IReadOnlyList<DocumentColumnMetadata>? columns = null,
        IReadOnlyList<DocumentIndexMetadata>? indexes = null)
        => new(name, TableKind.Head, columns ?? [], indexes ?? []);

    private static DocumentColumnMetadata Column(string name, bool required = false, int? maxLength = null)
        => new(name, ColumnType.String, required, maxLength);

    private static DbColumnSchema DbColumn(
        string table,
        string name,
        string type = "text",
        bool nullable = false,
        int? maxLength = null)
        => new(table, name, type, nullable, maxLength);

    private static DbSchemaSnapshot Snapshot(
        IReadOnlySet<string> tables,
        IReadOnlyDictionary<string, IReadOnlyList<DbColumnSchema>>? columns = null,
        IReadOnlyDictionary<string, IReadOnlyList<DbForeignKeySchema>>? foreignKeys = null,
        IReadOnlyDictionary<string, IReadOnlyList<DbIndexSchema>>? indexes = null)
        => new(
            tables,
            columns ?? new Dictionary<string, IReadOnlyList<DbColumnSchema>>(),
            foreignKeys ?? new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(),
            indexes ?? new Dictionary<string, IReadOnlyList<DbIndexSchema>>());
}
