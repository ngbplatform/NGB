using System.Data;
using System.Data.Common;
using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Catalogs;

public sealed class PostgresCatalogReaderFullCoverageTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Count_builds_catalog_only_and_head_criteria_queries()
    {
        var connection = Connection(scalar: sql => sql.Contains("JOIN", StringComparison.Ordinal) ? 9L : 4L);
        var sut = Reader(connection);

        var all = await sut.CountAsync(Head(), Query(), default);
        var searched = await sut.CountAsync(
            Head(),
            Query("  Acme  ", SoftDeleteFilterMode.Active, new CatalogFilter("status", "open")),
            default);
        var deleted = await sut.CountAsync(Head(), Query(null, SoftDeleteFilterMode.Deleted), default);

        all.Should().Be(4);
        searched.Should().Be(9);
        deleted.Should().Be(4);
        connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("FROM catalogs c", StringComparison.Ordinal)
            && !command.CommandText.Contains("JOIN", StringComparison.Ordinal));
        connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("h.\"name\" ILIKE", StringComparison.Ordinal)
            && command.CommandText.Contains("h.\"status\"::text = @f0", StringComparison.Ordinal)
            && command.CommandText.Contains("c.is_deleted = FALSE", StringComparison.Ordinal));
        connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("c.is_deleted = TRUE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Page_validates_bounds_and_maps_head_criteria_rows()
    {
        var connection = Connection(reader: _ => HeadRows(
            (FirstId, false, "Acme", "open", 12),
            (SecondId, true, null, null, null)));
        var sut = Reader(connection);

        Func<Task> negativeOffset = async () => await sut.GetPageAsync(Head(), Query(), -1, 10, default);
        Func<Task> zeroLimit = async () => await sut.GetPageAsync(Head(), Query(), 0, 0, default);
        await negativeOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var rows = await sut.GetPageAsync(
            Head(),
            Query("acme", SoftDeleteFilterMode.All, new CatalogFilter("status", "open")),
            2,
            10,
            default);

        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(new
        {
            Id = FirstId,
            IsMarkedForDeletion = false,
            Display = "Acme"
        });
        rows[0].Fields.Should().Contain("name", "Acme").And.Contain("status", "open").And.Contain("rank", 12);
        rows[1].Display.Should().BeNull();
        connection.Commands.Last().CommandText.Should().Contain("NULLS LAST").And.Contain("OFFSET @offset");
    }

    [Fact]
    public async Task Page_without_head_criteria_returns_full_non_null_page_without_counting()
    {
        var connection = Connection(reader: _ => HeadRows((FirstId, false, "Acme", "open", 1)));

        var rows = await Reader(connection).GetPageAsync(Head(), Query(), 0, 1, default);

        rows.Should().ContainSingle();
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("IS NOT NULL");
    }

    [Theory]
    [InlineData(2, 5, 0)]
    [InlineData(8, 3, 5)]
    public async Task Page_without_head_criteria_appends_null_and_missing_head_rows_with_correct_offset(
        int offset,
        long nonNullCount,
        long expectedNullOffset)
    {
        var connection = Connection(
            reader: sql => sql.Contains("IS NOT NULL", StringComparison.Ordinal)
                ? HeadRows()
                : HeadRows((SecondId, false, null, null, null)),
            scalar: _ => nonNullCount);

        var rows = await Reader(connection).GetPageAsync(Head(), Query(null, SoftDeleteFilterMode.Active), offset, 2, default);

        rows.Should().ContainSingle().Which.Id.Should().Be(SecondId);
        connection.Commands.Should().HaveCount(3);
        connection.Commands.Last().CommandText.Should().Contain("UNION ALL").And.Contain("NOT EXISTS");
        Parameter(connection.Commands.Last(), "nullOffset").Should().Be(expectedNullOffset);
        Parameter(connection.Commands.Last(), "remaining").Should().Be(2);
    }

    [Fact]
    public async Task Get_by_id_validates_id_and_returns_missing_or_mapped_row()
    {
        var missingConnection = Connection(reader: _ => HeadRows());
        var missingReader = Reader(missingConnection);
        Func<Task> emptyId = async () => await missingReader.GetByIdAsync(Head(), Guid.Empty, default);
        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await missingReader.GetByIdAsync(Head(), FirstId, default)).Should().BeNull();

        var rowWithoutDisplayAlias = new DataTable();
        rowWithoutDisplayAlias.Columns.Add("Id", typeof(Guid));
        rowWithoutDisplayAlias.Columns.Add("IsDeleted", typeof(bool));
        rowWithoutDisplayAlias.Columns.Add("status", typeof(string));
        rowWithoutDisplayAlias.Columns.Add("rank", typeof(int));
        rowWithoutDisplayAlias.Rows.Add(FirstId, true, "closed", 7);
        var found = await Reader(Connection(reader: _ => rowWithoutDisplayAlias.CreateDataReader()))
            .GetByIdAsync(Head(), FirstId, default);

        found.Should().NotBeNull();
        found!.Display.Should().BeNull();
        found.IsMarkedForDeletion.Should().BeTrue();
        found.Fields.Should().Contain("name", null).And.Contain("status", "closed");
    }

    [Fact]
    public async Task Lookup_covers_non_positive_limit_query_and_recent_modes()
    {
        var connection = Connection(reader: _ => LookupRows((FirstId, "Acme"), (SecondId, "Beta")));
        var sut = Reader(connection);

        (await sut.LookupAsync(Head(), "ignored", 0, default)).Should().BeEmpty();
        (await sut.LookupAsync(Head(), "  acme  ", 5, default)).Should().HaveCount(2);
        (await sut.LookupAsync(Head(), null, 5, default)).Should().HaveCount(2);

        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should().Contain("ILIKE").And.NotContain("UNION ALL");
        connection.Commands[1].CommandText.Should().Contain("UNION ALL").And.Contain("NOT EXISTS");
        Parameter(connection.Commands[0], "q").Should().Be("acme");
    }

    [Fact]
    public async Task Get_by_ids_skips_database_for_empty_input_and_preserves_requested_order_for_found_rows()
    {
        var connection = Connection(reader: _ => LookupRows((SecondId, "Beta"), (FirstId, "Acme")));
        var sut = Reader(connection);

        (await sut.GetByIdsAsync(Head(), [], default)).Should().BeEmpty();
        var rows = await sut.GetByIdsAsync(Head(), [FirstId, Guid.NewGuid(), SecondId, FirstId], default);

        rows.Select(row => row.Id).Should().Equal(FirstId, SecondId, FirstId);
        rows.Select(row => row.Label).Should().Equal("Acme", "Beta", "Acme");
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("ANY(");
    }

    [Fact]
    public async Task Across_types_validates_collection_and_short_circuits_empty_effective_inputs()
    {
        var sut = Reader(Connection());
        Func<Task> nullHeads = async () => await sut.LookupAcrossTypesAsync(null!, null, 1, false, default);
        await nullHeads.Should().ThrowAsync<NgbArgumentRequiredException>();

        (await sut.LookupAcrossTypesAsync([Head()], null, 0, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([null!], null, 1, false, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Across_types_deduplicates_heads_and_builds_query_and_browse_variants()
    {
        var connection = Connection(reader: _ => AcrossRows(
            (FirstId, "customers", "Acme", false),
            (SecondId, "vendors", null, true)));
        var sut = Reader(connection);
        var customers = Head("customers", "cat_customers", "name");
        var vendors = Head("vendors", "cat_\"vendors", "title");

        var searched = await sut.LookupAcrossTypesAsync(
            [customers, customers with { CatalogCode = "CUSTOMERS" }, vendors],
            "  inc  ",
            3,
            true,
            default);
        var browsed = await sut.LookupAcrossTypesAsync([customers], null, 2, false, default);

        searched.Should().HaveCount(2);
        searched[1].Should().BeEquivalentTo(new CatalogLookupSearchRow(SecondId, "vendors", null, true));
        browsed.Should().HaveCount(2);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should()
            .Contain("UNION ALL")
            .And.Contain("LEFT JOIN")
            .And.Contain("c.is_deleted = FALSE")
            .And.Contain("ILIKE")
            .And.Contain("\"cat_\"\"vendors\"");
        connection.Commands[1].CommandText.Should().Contain("JOIN catalogs").And.NotContain("ILIKE");
        Parameter(connection.Commands[0], "q").Should().Be("inc");
    }

    [Theory]
    [InlineData("", "cat_customers", "name", "CatalogCode")]
    [InlineData("customers", " ", "name", "HeadTableName")]
    [InlineData("customers", "cat_customers", "\t", "DisplayColumn")]
    public async Task Metadata_requires_catalog_table_and_display_identifiers(
        string catalogCode,
        string table,
        string display,
        string expectedParameter)
    {
        var sut = Reader(Connection());

        Func<Task> act = async () => await sut.CountAsync(Head(catalogCode, table, display), Query(), default);

        var error = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        error.Which.ParamName.Should().Be(expectedParameter);
    }

    [Fact]
    public async Task Empty_filter_identifier_is_rejected_and_unknown_soft_delete_mode_behaves_as_all()
    {
        var sut = Reader(Connection(scalar: _ => 1L));
        Func<Task> emptyIdentifier = async () => await sut.CountAsync(
            Head(), Query(null, SoftDeleteFilterMode.All, new CatalogFilter(" ", "x")), default);
        await emptyIdentifier.Should().ThrowAsync<NgbArgumentInvalidException>();

        var result = await sut.CountAsync(Head(), Query(null, (SoftDeleteFilterMode)999), default);
        result.Should().Be(1);

        var nullFilters = await sut.CountAsync(
            Head(),
            new CatalogQuery(null, null!) { SoftDeleteFilterMode = SoftDeleteFilterMode.All },
            default);
        nullFilters.Should().Be(1);
    }

    private static PostgresCatalogReader Reader(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection));

    private static RecordingDbConnection Connection(
        Func<string, DbDataReader>? reader = null,
        Func<string, object?>? scalar = null)
        => new(readerFactory: reader, scalar: scalar);

    private static CatalogHeadDescriptor Head(
        string catalogCode = "customers",
        string table = "cat_customers",
        string display = "name")
        => new(
            catalogCode,
            table,
            display,
            [
                new CatalogHeadColumn("NAME", ColumnType.String),
                new CatalogHeadColumn("status", ColumnType.String),
                new CatalogHeadColumn("rank", ColumnType.Int32)
            ]);

    private static CatalogQuery Query(
        string? search = null,
        SoftDeleteFilterMode mode = SoftDeleteFilterMode.All,
        params CatalogFilter[] filters)
        => new(search, filters) { SoftDeleteFilterMode = mode };

    private static object? Parameter(RecordingDbCommand command, string name)
        => command.ParametersSnapshot.Single(parameter => parameter.ParameterName == name).Value;

    private static DbDataReader HeadRows(params (Guid Id, bool Deleted, string? Display, string? Status, int? Rank)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("IsDeleted", typeof(bool));
        table.Columns.Add("Display", typeof(object));
        table.Columns.Add("NAME", typeof(object));
        table.Columns.Add("status", typeof(object));
        table.Columns.Add("rank", typeof(object));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                row.Deleted,
                row.Display ?? (object)DBNull.Value,
                row.Display ?? (object)DBNull.Value,
                row.Status ?? (object)DBNull.Value,
                row.Rank ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private static DbDataReader LookupRows(params (Guid Id, string Label)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Label", typeof(string));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.Label);

        return table.CreateDataReader();
    }

    private static DbDataReader AcrossRows(params (Guid Id, string Code, string? Label, bool Deleted)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("CatalogCode", typeof(string));
        table.Columns.Add("Label", typeof(object));
        table.Columns.Add("IsMarkedForDeletion", typeof(bool));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.Code, row.Label ?? (object)DBNull.Value, row.Deleted);

        return table.CreateDataReader();
    }
}
