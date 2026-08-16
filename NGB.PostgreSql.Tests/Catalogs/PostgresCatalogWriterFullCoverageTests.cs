using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Persistence.Catalogs.Universal;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Catalogs;

public sealed class PostgresCatalogWriterFullCoverageTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task UpsertHeads_validates_rows_and_returns_early_for_empty_batches_or_no_values()
    {
        var inactive = new PostgresCatalogWriter(new RecordingUnitOfWork(new RecordingDbConnection()));
        Func<Task> nullRows = () => inactive.UpsertHeadsAsync(Head(), null!);
        await nullRows.Should().ThrowAsync<NgbArgumentRequiredException>();
        await inactive.UpsertHeadsAsync(Head(), []);
        Func<Task> noTransaction = () => inactive.UpsertHeadsAsync(
            Head(), [new CatalogHeadWriteRow(FirstId, [new("name", ColumnType.String, "A")])]);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>();

        var fixture = Fixture();
        await fixture.Writer.UpsertHeadsAsync(Head(), [new CatalogHeadWriteRow(FirstId, [])]);
        fixture.Connection.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Normalize_rejects_invalid_ids_values_names_unknown_columns_type_mismatches_and_duplicates()
    {
        var sut = Fixture().Writer;
        var head = Head();
        Func<Task> emptyId = () => sut.UpsertHeadsAsync(head, [new CatalogHeadWriteRow(Guid.Empty, [])]);
        Func<Task> nullValues = () => sut.UpsertHeadsAsync(head, [new CatalogHeadWriteRow(FirstId, null!)]);
        Func<Task> emptyName = () => sut.UpsertHeadsAsync(
            head, [new CatalogHeadWriteRow(FirstId, [new(" ", ColumnType.String, "A")])]);
        Func<Task> unknown = () => sut.UpsertHeadsAsync(
            head, [new CatalogHeadWriteRow(FirstId, [new("unknown", ColumnType.String, "A")])]);
        Func<Task> mismatch = () => sut.UpsertHeadsAsync(
            head, [new CatalogHeadWriteRow(FirstId, [new("name", ColumnType.Guid, FirstId)])]);
        Func<Task> duplicate = () => sut.UpsertHeadsAsync(
            head,
            [new CatalogHeadWriteRow(FirstId, [new("name", ColumnType.String, "A"), new("NAME", ColumnType.String, "B")])]);

        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullValues.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyName.Should().ThrowAsync<NgbArgumentInvalidException>();
        await unknown.Should().ThrowAsync<NgbArgumentInvalidException>();
        await mismatch.Should().ThrowAsync<NgbArgumentInvalidException>();
        await duplicate.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task UpsertHead_and_batch_cover_every_supported_column_type_sparse_values_json_cast_and_identifier_quoting()
    {
        var columns = new[]
        {
            new CatalogHeadColumn("name", ColumnType.String),
            new CatalogHeadColumn("payload", ColumnType.Json),
            new CatalogHeadColumn("owner_id", ColumnType.Guid),
            new CatalogHeadColumn("rank", ColumnType.Int32),
            new CatalogHeadColumn("sequence", ColumnType.Int64),
            new CatalogHeadColumn("amount", ColumnType.Decimal),
            new CatalogHeadColumn("enabled", ColumnType.Boolean),
            new CatalogHeadColumn("effective_on", ColumnType.Date),
            new CatalogHeadColumn("updated_at", ColumnType.DateTimeUtc),
            new CatalogHeadColumn("unused", ColumnType.String)
        };
        var head = new CatalogHeadDescriptor("products", "cat_\"products", "name", columns);
        var values = new CatalogHeadValue[]
        {
            new("name", ColumnType.String, "Widget"),
            new("payload", ColumnType.Json, "{\"color\":\"red\"}"),
            new("owner_id", ColumnType.Guid, FirstId),
            new("rank", ColumnType.Int32, 7),
            new("sequence", ColumnType.Int64, 8L),
            new("amount", ColumnType.Decimal, 12.5m),
            new("enabled", ColumnType.Boolean, true),
            new("effective_on", ColumnType.Date, new DateOnly(2026, 8, 1)),
            new("updated_at", ColumnType.DateTimeUtc, new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc))
        };
        var fixture = Fixture();

        await fixture.Writer.UpsertHeadAsync(head, FirstId, values);
        await fixture.Writer.UpsertHeadsAsync(
            head,
            [new CatalogHeadWriteRow(FirstId, values), new CatalogHeadWriteRow(SecondId, [])]);

        fixture.Connection.Commands.Should().HaveCount(2);
        var sql = fixture.Connection.Commands.Last().CommandText;
        sql.Should().Contain("INSERT INTO \"cat_\"\"products\"");
        sql.Should().Contain("::jsonb");
        sql.Should().Contain("::text[]");
        sql.Should().Contain("::uuid[]");
        sql.Should().Contain("::integer[]");
        sql.Should().Contain("::bigint[]");
        sql.Should().Contain("::numeric[]");
        sql.Should().Contain("::boolean[]");
        sql.Should().Contain("::date[]");
        sql.Should().Contain("::timestamptz[]");
        sql.Should().NotContain("\"unused\"");
    }

    [Fact]
    public void Pure_column_helpers_reject_unsupported_types_and_quote_identifiers()
    {
        var unsupported = (ColumnType)999;
        Action create = () => PostgresCatalogWriter.CreateColumnArray(unsupported, 1);
        Action set = () => PostgresCatalogWriter.SetArrayValue(new object?[1], 0, null, unsupported);
        Action sqlType = () => PostgresCatalogWriter.ToArraySqlType(unsupported);
        Action emptyIdentifier = () => PostgresCatalogWriter.Qi(" ");
        create.Should().Throw<NgbArgumentInvalidException>();
        set.Should().Throw<NgbArgumentInvalidException>();
        sqlType.Should().Throw<NgbArgumentInvalidException>();
        emptyIdentifier.Should().Throw<NgbArgumentInvalidException>();
        PostgresCatalogWriter.Qi("a\"b").Should().Be("\"a\"\"b\"");
    }

    private static CatalogHeadDescriptor Head()
        => new("products", "cat_products", "name", [new("name", ColumnType.String)]);

    private static FixtureState Fixture() => new();

    private sealed class FixtureState
    {
        public RecordingDbConnection Connection { get; } = new();
        public PostgresCatalogWriter Writer => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true));
    }
}
