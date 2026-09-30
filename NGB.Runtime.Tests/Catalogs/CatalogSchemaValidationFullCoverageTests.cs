using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Schema;
using NGB.Runtime.Catalogs;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class CatalogSchemaValidationFullCoverageTests
{
    [Fact]
    public async Task DiagnoseAndValidate_CoverEveryErrorWarningAndComparisonBranch()
    {
        var metadata = Meta([
            Table("missing-table"),
            Table("no-columns"),
            Table("missing-id", [Column("name", required: true)]),
            Table("nullable-id"),
            Table("no-index-snapshot", indexes: [new("missing-index-snapshot", ["catalog_id"]) ]),
            Table("bad-values",
                [
                    Column("required_bad", required: true, maxLength: 10),
                    Column("max_unknown", maxLength: 10),
                    Column("max_ok", maxLength: 10),
                    Column("no_max")
                ],
                [
                    new("missing", ["required_bad"]),
                    new("unique", ["required_bad"], Unique: true),
                    new("length", ["required_bad", "max_ok"]),
                    new("value", ["required_bad"]),
                    new("okay", ["REQUIRED_BAD"], Unique: true),
                    new("empty", [])
                ])
        ]);
        var snapshot = Snapshot(
            tables: new HashSet<string>(["no-columns", "missing-id", "nullable-id", "no-index-snapshot", "bad-values"]),
            columns: new Dictionary<string, IReadOnlyList<DbColumnSchema>>
            {
                ["missing-id"] = [],
                ["nullable-id"] = [DbColumn("nullable-id", "catalog_id", nullable: true)],
                ["no-index-snapshot"] = [DbColumn("no-index-snapshot", "catalog_id")],
                ["bad-values"] =
                [
                    DbColumn("bad-values", "catalog_id"),
                    DbColumn("bad-values", "catalog_id"),
                    DbColumn("bad-values", "required_bad", type: "bad", nullable: true, maxLength: 5),
                    DbColumn("bad-values", "max_unknown", maxLength: null),
                    DbColumn("bad-values", "max_ok", maxLength: 10),
                    DbColumn("bad-values", "no_max")
                ]
            },
            foreignKeys: new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>
            {
                ["nullable-id"] = [new("nullable-id", "fk-wrong", "other", "catalogs", "id")],
                ["bad-values"] = [new("bad-values", "fk", "CATALOG_ID", "CATALOGS", "ID")]
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
        var sut = Service([metadata], snapshot, storage: null);

        var result = await sut.DiagnoseAllAsync(default);

        result.Errors.Should().Contain(x => x.Contains("missing ICatalogTypeStorage", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("does not exist", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("cannot read columns", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("must have column 'catalog_id'", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("catalog_id must be NOT NULL", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("must have FK", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("missing column 'name'", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("required_bad must be NOT NULL", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("has type 'bad'", StringComparison.Ordinal));
        result.Errors.Should().Contain(x => x.Contains("max length is 5", StringComparison.Ordinal));
        result.Warnings.Should().Contain(x => x.Contains("missing index", StringComparison.Ordinal));
        result.Warnings.Should().Contain(x => x.Contains("should be UNIQUE", StringComparison.Ordinal));
        result.Warnings.Count(x => x.Contains("columns mismatch", StringComparison.Ordinal)).Should().Be(2);

        await ((Func<Task>)(() => sut.ValidateAllAsync(default)))
            .Should().ThrowAsync<CatalogSchemaValidationException>();
    }

    [Fact]
    public async Task ValidateAll_AllowsHealthyTableStorageAndEmptyTableMetadata()
    {
        var healthy = Meta([Table("healthy", [Column("name", required: true, maxLength: 10)])]);
        var declarationOnly = Meta([], "empty");
        var snapshot = Snapshot(
            tables: new HashSet<string>(["healthy"]),
            columns: new Dictionary<string, IReadOnlyList<DbColumnSchema>>
            {
                ["healthy"] =
                [
                    DbColumn("healthy", "catalog_id"),
                    DbColumn("healthy", "name", nullable: false, maxLength: 20)
                ]
            },
            foreignKeys: new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>
            {
                ["healthy"] = [new("healthy", "fk", "catalog_id", "catalogs", "id")]
            });
        var storage = Mock.Of<ICatalogTypeStorage>();
        var sut = Service([healthy, declarationOnly], snapshot, storage);

        await sut.ValidateAllAsync(default);
        (await sut.DiagnoseAllAsync(default)).HasErrors.Should().BeFalse();
    }

    private static CatalogSchemaValidationService Service(
        IReadOnlyCollection<CatalogTypeMetadata> metadata,
        DbSchemaSnapshot snapshot,
        ICatalogTypeStorage? storage)
    {
        var registry = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.All()).Returns(metadata);
        var schema = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        schema.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var mapper = new Mock<IDbTypeMapper>(MockBehavior.Strict);
        mapper.Setup(x => x.IsCompatible(It.IsAny<ColumnType>(), It.IsAny<string>()))
            .Returns<ColumnType, string>((_, dbType) => dbType != "bad");
        mapper.Setup(x => x.GetExpectedDbType(It.IsAny<ColumnType>())).Returns("text");
        var storages = new Mock<ICatalogTypeStorageResolver>(MockBehavior.Strict);
        storages.Setup(x => x.TryResolve(It.IsAny<string>())).Returns(storage);
        return new CatalogSchemaValidationService(
            registry.Object,
            schema.Object,
            mapper.Object,
            storages.Object,
            NullLogger<CatalogSchemaValidationService>.Instance);
    }

    private static CatalogTypeMetadata Meta(
        IReadOnlyList<CatalogTableMetadata> tables,
        string code = "cat")
        => new(code, code, tables, new CatalogPresentationMetadata("table", "name"), new CatalogMetadataVersion(1, "hash"));

    private static CatalogTableMetadata Table(
        string name,
        IReadOnlyList<CatalogColumnMetadata>? columns = null,
        IReadOnlyList<CatalogIndexMetadata>? indexes = null)
        => new(name, TableKind.Head, columns ?? [], indexes ?? []);

    private static CatalogColumnMetadata Column(
        string name,
        bool required = false,
        int? maxLength = null)
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
