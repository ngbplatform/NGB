using System.Data;
using System.Data.Common;
using Dapper;
using FluentAssertions;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Persistence.Common;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Documents;

public sealed class PostgresDocumentReaderFullCoverageTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Count_builds_base_head_soft_delete_filter_contributor_and_period_clauses()
    {
        var connection = Connection(scalar: sql => sql.Contains("JOIN", StringComparison.Ordinal) ? 9L : 4L);
        var sut = Reader(connection, new StubContributor(false), new StubContributor(true));

        var all = await sut.CountAsync(Head(), Query(), default);
        var searched = await sut.CountAsync(
            Head(),
            Query(
                "  invoice  ",
                SoftDeleteFilterMode.Active,
                new DocumentFilter("amount", ["12.5"], ColumnType.Decimal, "amount")) with
            {
                PeriodFilter = new DocumentPeriodFilter(
                    "document_date",
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 12, 31))
            },
            default);
        var contributed = await sut.CountAsync(
            Head(),
            Query(null, SoftDeleteFilterMode.Deleted, new DocumentFilter("workflow", ["open"], ColumnType.String)),
            default);
        var emptyPeriod = await sut.CountAsync(
            Head(),
            Query(null, (SoftDeleteFilterMode)999) with
            {
                PeriodFilter = new DocumentPeriodFilter("document_date", null, null)
            },
            default);

        all.Should().Be(4);
        searched.Should().Be(9);
        contributed.Should().Be(9);
        emptyPeriod.Should().Be(4);
        connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("h.\"name\" ILIKE", StringComparison.Ordinal)
            && command.CommandText.Contains("h.\"amount\" = @f0", StringComparison.Ordinal)
            && command.CommandText.Contains("::date >= @periodFrom", StringComparison.Ordinal)
            && command.CommandText.Contains("::date <= @periodTo", StringComparison.Ordinal)
            && command.CommandText.Contains("d.status <> @deletedStatus", StringComparison.Ordinal));
        connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("d.workflow_state = @f0", StringComparison.Ordinal)
            && command.CommandText.Contains("d.status = @deletedStatus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unmapped_document_filter_is_a_configuration_error()
    {
        var sut = Reader(Connection(), new StubContributor(false));

        Func<Task> act = async () => await sut.CountAsync(
            Head(),
            Query(null, SoftDeleteFilterMode.All, new DocumentFilter("unknown", ["x"], ColumnType.String)),
            default);

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.Context.Should().Contain("documentType", "invoice").And.Contain("filterKey", "unknown");
    }

    [Fact]
    public async Task Page_validates_bounds_maps_short_and_int_statuses_and_rejects_unknown_status_type()
    {
        var connection = Connection(reader: _ => HeadRows(
            (FirstId, (short)DocumentStatus.Draft, "INV-1", "Acme", 12.5m),
            (SecondId, (int)DocumentStatus.MarkedForDeletion, null, null, null)));
        var sut = Reader(connection);
        Func<Task> negativeOffset = async () => await sut.GetPageAsync(Head(), Query(), -1, 10, default);
        Func<Task> zeroLimit = async () => await sut.GetPageAsync(Head(), Query(), 0, 0, default);
        await negativeOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var rows = await sut.GetPageAsync(Head(), Query("invoice"), 2, 10, default);
        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(new
        {
            Id = FirstId,
            Status = DocumentStatus.Draft,
            IsMarkedForDeletion = false,
            Display = "Acme",
            Number = "INV-1"
        });
        rows[0].Fields.Should().Contain("name", "Acme").And.Contain("amount", 12.5m);
        rows[1].Status.Should().Be(DocumentStatus.MarkedForDeletion);
        rows[1].IsMarkedForDeletion.Should().BeTrue();

        var badStatus = Reader(Connection(reader: _ => HeadRows((FirstId, "draft", null, null, null))));
        Func<Task> invalidStatus = async () => await badStatus.GetByIdAsync(Head(), FirstId, default);
        var error = await invalidStatus.Should().ThrowAsync<NgbInvariantViolationException>();
        error.Which.Context.Should().Contain("type", typeof(string).FullName);
    }

    [Fact]
    public async Task Page_without_head_criteria_returns_full_non_null_page_without_counting()
    {
        var connection = Connection(reader: _ => HeadRows(
            (FirstId, (short)DocumentStatus.Posted, "INV-1", "Acme", 1m)));

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
                : HeadRows((SecondId, (short)DocumentStatus.Draft, "INV-2", null, null)),
            scalar: _ => nonNullCount);

        var rows = await Reader(connection).GetPageAsync(Head(), Query(null, SoftDeleteFilterMode.Active), offset, 2, default);

        rows.Should().ContainSingle().Which.Id.Should().Be(SecondId);
        connection.Commands.Should().HaveCount(3);
        connection.Commands.Last().CommandText.Should().Contain("UNION ALL").And.Contain("NOT EXISTS");
        Parameter(connection.Commands.Last(), "nullOffset").Should().Be(expectedNullOffset);
        Parameter(connection.Commands.Last(), "remaining").Should().Be(2);
    }

    [Fact]
    public async Task Combined_page_validates_bounds_and_returns_total_and_rows_in_one_round_trip()
    {
        var connection = Connection(reader: _ => CombinedHeadRows(
            11,
            (FirstId, (short)DocumentStatus.Posted, "INV-1", "Acme", 12.5m)));
        var sut = Reader(connection);

        await ((Func<Task>)(() => sut.GetPageWithTotalAsync(Head(), Query(), -1, 10)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetPageWithTotalAsync(Head(), Query(), 0, 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var page = await sut.GetPageWithTotalAsync(Head(), Query(), 3, 10);
        var filtered = await sut.GetPageWithTotalAsync(
            Head(),
            Query("invoice", SoftDeleteFilterMode.Active),
            0,
            5);

        page.Total.Should().Be(11);
        page.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = FirstId,
            Status = DocumentStatus.Posted,
            Display = "Acme",
            Number = "INV-1"
        });
        filtered.Total.Should().Be(11);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should()
            .Contain("COUNT(*) OVER()")
            .And.Contain("UNION ALL")
            .And.Contain("NOT EXISTS")
            .And.Contain("ORDER BY \"SortDisplay\" NULLS LAST");
        connection.Commands[1].CommandText.Should()
            .Contain("h.\"name\" ILIKE")
            .And.NotContain("UNION ALL");
        Parameter(connection.Commands[0], "offset").Should().Be(3);
        Parameter(connection.Commands[0], "limit").Should().Be(10);
    }

    [Fact]
    public async Task Combined_page_beyond_the_end_uses_a_count_fallback_for_exact_total()
    {
        var connection = Connection(
            reader: _ => CombinedHeadRows(0),
            scalar: _ => 11L);

        var page = await Reader(connection).GetPageWithTotalAsync(Head(), Query(), 50, 10);

        page.Rows.Should().BeEmpty();
        page.Total.Should().Be(11);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should().Contain("COUNT(*) OVER()");
        connection.Commands[1].CommandText.Should().Contain("SELECT COUNT(*)");
    }

    [Fact]
    public async Task Get_by_id_validates_id_and_returns_null_or_a_row_with_absent_optional_aliases()
    {
        var missing = Reader(Connection(reader: _ => HeadRows()));
        Func<Task> emptyId = async () => await missing.GetByIdAsync(Head(), Guid.Empty, default);
        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await missing.GetByIdAsync(Head(), FirstId, default)).Should().BeNull();

        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Status", typeof(object));
        table.Columns.Add("amount", typeof(decimal));
        table.Rows.Add(FirstId, (short)DocumentStatus.Posted, 3m);
        var found = await Reader(Connection(reader: _ => table.CreateDataReader())).GetByIdAsync(Head(), FirstId, default);

        found.Should().NotBeNull();
        found!.Display.Should().BeNull();
        found.Number.Should().BeNull();
        found.Fields.Should().Contain("name", null).And.Contain("amount", 3m);
    }

    [Fact]
    public async Task Get_by_ids_short_circuits_empty_inputs_deduplicates_ids_and_maps_rows()
    {
        var connection = Connection(reader: _ => HeadRows(
            (FirstId, (short)DocumentStatus.Posted, "INV-1", "Acme", 1m)));
        var sut = Reader(connection);

        (await sut.GetByIdsAsync(Head(), [], default)).Should().BeEmpty();
        (await sut.GetByIdsAsync(Head(), [Guid.Empty, Guid.Empty], default)).Should().BeEmpty();
        var rows = await sut.GetByIdsAsync(Head(), [Guid.Empty, FirstId, FirstId], default);

        rows.Should().ContainSingle().Which.Id.Should().Be(FirstId);
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("ANY(");
    }

    [Fact]
    public async Task Head_rows_across_types_validates_and_short_circuits_each_empty_effective_input()
    {
        var sut = Reader(Connection());
        Func<Task> nullHeads = async () => await sut.GetHeadRowsByIdsAcrossTypesAsync(null!, [FirstId], default);
        Func<Task> nullIds = async () => await sut.GetHeadRowsByIdsAcrossTypesAsync([Head()], null!, default);
        await nullHeads.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();

        (await sut.GetHeadRowsByIdsAcrossTypesAsync([], [FirstId], default)).Should().BeEmpty();
        (await sut.GetHeadRowsByIdsAcrossTypesAsync([Head()], [], default)).Should().BeEmpty();
        (await sut.GetHeadRowsByIdsAcrossTypesAsync([null!], [FirstId], default)).Should().BeEmpty();
        (await sut.GetHeadRowsByIdsAcrossTypesAsync([Head()], [Guid.Empty], default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Head_rows_across_types_builds_json_for_distinct_heads_and_converts_every_column_type()
    {
        var richHead = RichHead();
        var emptyHead = Head("memo", "doc_memo", "title") with { Columns = [] };
        const string json = """
                            {
                              "name": "Acme",
                              "i32": 32,
                              "i64": 9223372036854770000,
                              "decimal": 12.34,
                              "boolean": true,
                              "guid": "33333333-3333-3333-3333-333333333333",
                              "date": "2026-08-16",
                              "utc": "2026-08-16T12:34:56-04:00",
                              "json": { "nested": 1 },
                              "nullable": null,
                              "o'clock": "quoted"
                            }
                            """;
        var connection = Connection(reader: _ => AcrossHeadRows(
            (FirstId, "invoice", DocumentStatus.Posted, "Acme", "INV-1", json),
            (SecondId, "invoice", DocumentStatus.MarkedForDeletion, null, null, " ")));
        var sut = Reader(connection);

        var rows = await sut.GetHeadRowsByIdsAcrossTypesAsync(
            [richHead, richHead with { TypeCode = "INVOICE" }, emptyHead],
            [Guid.Empty, FirstId, FirstId, SecondId],
            default);

        rows.Should().HaveCount(2);
        rows[0].Fields.Should()
            .Contain("name", "Acme")
            .And.Contain("i32", 32)
            .And.Contain("i64", 9223372036854770000L)
            .And.Contain("decimal", 12.34m)
            .And.Contain("boolean", true)
            .And.Contain("guid", Guid.Parse("33333333-3333-3333-3333-333333333333"))
            .And.Contain("date", new DateOnly(2026, 8, 16))
            .And.Contain("nullable", null)
            .And.Contain("missing", null)
            .And.Contain("o'clock", "quoted");
        rows[0].Fields["utc"].Should().BeOfType<DateTime>().Which.Kind.Should().Be(DateTimeKind.Utc);
        rows[0].Fields["json"].Should().Be("{ \"nested\": 1 }");
        rows[0].IsMarkedForDeletion.Should().BeFalse();
        rows[1].Fields.Values.Should().OnlyContain(value => value == null);
        rows[1].IsMarkedForDeletion.Should().BeTrue();
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("UNION ALL")
            .And.Contain("jsonb_build_object")
            .And.Contain("o''clock")
            .And.Contain("'{}'::jsonb");
    }

    [Fact]
    public async Task Lookup_across_types_validates_shortcuts_deduplicates_and_builds_search_and_browse_sql()
    {
        var emptyReader = Reader(Connection());
        Func<Task> nullHeads = async () => await emptyReader.LookupAcrossTypesAsync(null!, null, 1, false, default);
        await nullHeads.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await emptyReader.LookupAcrossTypesAsync([Head()], null, 0, false, default)).Should().BeEmpty();
        (await emptyReader.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        (await emptyReader.LookupAcrossTypesAsync([null!], null, 1, false, default)).Should().BeEmpty();

        var connection = Connection(reader: _ => LookupRows(
            (FirstId, "invoice", DocumentStatus.Posted, false, "Acme", "INV-1"),
            (SecondId, "memo", DocumentStatus.MarkedForDeletion, true, null, null)));
        var sut = Reader(connection);
        var invoice = Head();
        var memo = Head("memo", "doc_\"memo", "title");
        var searched = await sut.LookupAcrossTypesAsync(
            [invoice, invoice with { TypeCode = "INVOICE" }, memo], FirstId.ToString(), 3, true, default);
        var browsed = await sut.LookupAcrossTypesAsync([invoice], null, 2, false, default);

        searched.Should().HaveCount(2);
        searched[1].Should().BeEquivalentTo(
            new DocumentLookupRow(SecondId, "memo", DocumentStatus.MarkedForDeletion, true, null, null));
        browsed.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should()
            .Contain("UNION ALL")
            .And.Contain("LEFT JOIN")
            .And.Contain("d.status <> @deletedStatus")
            .And.Contain("ILIKE")
            .And.NotContain("COALESCE(h.\"name\", d.id::text) ILIKE")
            .And.NotContain("d.id::text ILIKE")
            .And.Contain("\"doc_\"\"memo\"");
        connection.Commands[1].CommandText.Should().Contain("JOIN documents").And.NotContain("ILIKE");
        Parameter(connection.Commands[0], "q").Should().Be(FirstId.ToString());
        Parameter(connection.Commands[0], "queryId").Should().Be(FirstId);
    }

    [Fact]
    public async Task Lookup_by_ids_across_types_validates_shortcuts_and_maps_distinct_effective_inputs()
    {
        var emptyReader = Reader(Connection());
        Func<Task> nullHeads = async () => await emptyReader.GetByIdsAcrossTypesAsync(null!, [FirstId], default);
        Func<Task> nullIds = async () => await emptyReader.GetByIdsAcrossTypesAsync([Head()], null!, default);
        await nullHeads.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await emptyReader.GetByIdsAcrossTypesAsync([], [FirstId], default)).Should().BeEmpty();
        (await emptyReader.GetByIdsAcrossTypesAsync([Head()], [], default)).Should().BeEmpty();
        (await emptyReader.GetByIdsAcrossTypesAsync([null!], [FirstId], default)).Should().BeEmpty();
        (await emptyReader.GetByIdsAcrossTypesAsync([Head()], [Guid.Empty], default)).Should().BeEmpty();

        var connection = Connection(reader: _ => LookupRows(
            (FirstId, "invoice", DocumentStatus.Draft, false, "Acme", "INV-1")));
        var rows = await Reader(connection).GetByIdsAcrossTypesAsync(
            [Head(), Head() with { TypeCode = "INVOICE" }, Head("memo", "doc_memo", "title")],
            [Guid.Empty, FirstId, FirstId],
            default);

        rows.Should().ContainSingle().Which.Id.Should().Be(FirstId);
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("UNION ALL").And.Contain("ANY(");
    }

    [Fact]
    public async Task Count_accepts_null_filter_collection_as_no_filters()
    {
        var connection = Connection(scalar: _ => 1L);

        var count = await Reader(connection).CountAsync(
            Head(),
            new DocumentQuery(null, null!) { SoftDeleteFilterMode = SoftDeleteFilterMode.All },
            default);

        count.Should().Be(1);
        connection.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "doc_invoice", "name", "TypeCode")]
    [InlineData("invoice", " ", "name", "HeadTableName")]
    [InlineData("invoice", "doc_invoice", "\t", "DisplayColumn")]
    public async Task Metadata_requires_type_table_and_display_identifiers(
        string typeCode,
        string table,
        string display,
        string expectedParameter)
    {
        Func<Task> act = async () => await Reader(Connection()).CountAsync(
            Head(typeCode, table, display), Query(), default);

        var error = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        error.Which.ParamName.Should().Be(expectedParameter);
    }

    private static PostgresDocumentReader Reader(
        RecordingDbConnection connection,
        params IPostgresDocumentListFilterSqlContributor[] contributors)
        => new(new RecordingUnitOfWork(connection), contributors);

    private static RecordingDbConnection Connection(
        Func<string, DbDataReader>? reader = null,
        Func<string, object?>? scalar = null)
        => new(readerFactory: reader, scalar: scalar);

    private static DocumentHeadDescriptor Head(
        string typeCode = "invoice",
        string table = "doc_invoice",
        string display = "name")
        => new(
            typeCode,
            table,
            display,
            [
                new DocumentHeadColumn("NAME", ColumnType.String),
                new DocumentHeadColumn("amount", ColumnType.Decimal)
            ]);

    private static DocumentHeadDescriptor RichHead()
        => new(
            "invoice",
            "doc_invoice",
            "name",
            [
                new DocumentHeadColumn("name", ColumnType.String),
                new DocumentHeadColumn("i32", ColumnType.Int32),
                new DocumentHeadColumn("i64", ColumnType.Int64),
                new DocumentHeadColumn("decimal", ColumnType.Decimal),
                new DocumentHeadColumn("boolean", ColumnType.Boolean),
                new DocumentHeadColumn("guid", ColumnType.Guid),
                new DocumentHeadColumn("date", ColumnType.Date),
                new DocumentHeadColumn("utc", ColumnType.DateTimeUtc),
                new DocumentHeadColumn("json", ColumnType.Json),
                new DocumentHeadColumn("nullable", ColumnType.String),
                new DocumentHeadColumn("missing", ColumnType.String),
                new DocumentHeadColumn("o'clock", ColumnType.String)
            ]);

    private static DocumentQuery Query(
        string? search = null,
        SoftDeleteFilterMode mode = SoftDeleteFilterMode.All,
        params DocumentFilter[] filters)
        => new(search, filters) { SoftDeleteFilterMode = mode };

    private static object? Parameter(RecordingDbCommand command, string name)
        => command.ParametersSnapshot.Single(parameter => parameter.ParameterName == name).Value;

    private static DbDataReader HeadRows(
        params (Guid Id, object Status, string? Number, string? Display, decimal? Amount)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Status", typeof(object));
        table.Columns.Add("Number", typeof(object));
        table.Columns.Add("Display", typeof(object));
        table.Columns.Add("NAME", typeof(object));
        table.Columns.Add("amount", typeof(object));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                row.Status,
                row.Number ?? (object)DBNull.Value,
                row.Display ?? (object)DBNull.Value,
                row.Display ?? (object)DBNull.Value,
                row.Amount ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private static DbDataReader CombinedHeadRows(
        long total,
        params (Guid Id, object Status, string? Number, string? Display, decimal? Amount)[] rows)
    {
        var page = new DataTable();
        page.Columns.Add("Id", typeof(Guid));
        page.Columns.Add("Status", typeof(object));
        page.Columns.Add("Number", typeof(object));
        page.Columns.Add("Display", typeof(object));
        page.Columns.Add("NAME", typeof(object));
        page.Columns.Add("amount", typeof(object));
        page.Columns.Add("TotalCount", typeof(long));
        foreach (var row in rows)
        {
            page.Rows.Add(
                row.Id,
                row.Status,
                row.Number ?? (object)DBNull.Value,
                row.Display ?? (object)DBNull.Value,
                row.Display ?? (object)DBNull.Value,
                row.Amount ?? (object)DBNull.Value,
                total);
        }

        return page.CreateDataReader();
    }

    private static DbDataReader AcrossHeadRows(
        params (Guid Id, string TypeCode, DocumentStatus Status, string? Display, string? Number, string? FieldsJson)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Status", typeof(short));
        table.Columns.Add("Display", typeof(object));
        table.Columns.Add("Number", typeof(object));
        table.Columns.Add("FieldsJson", typeof(object));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                row.TypeCode,
                (short)row.Status,
                row.Display ?? (object)DBNull.Value,
                row.Number ?? (object)DBNull.Value,
                row.FieldsJson ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private static DbDataReader LookupRows(
        params (Guid Id, string TypeCode, DocumentStatus Status, bool Deleted, string? Label, string? Number)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Status", typeof(short));
        table.Columns.Add("IsMarkedForDeletion", typeof(bool));
        table.Columns.Add("Label", typeof(object));
        table.Columns.Add("Number", typeof(object));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                row.TypeCode,
                (short)row.Status,
                row.Deleted,
                row.Label ?? (object)DBNull.Value,
                row.Number ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private sealed class StubContributor(bool handles) : IPostgresDocumentListFilterSqlContributor
    {
        public bool TryBuildClause(
            DocumentHeadDescriptor head,
            DocumentFilter filter,
            string documentAlias,
            string headAlias,
            string parameterName,
            DynamicParameters parameters,
            out string clause)
        {
            if (!handles)
            {
                clause = string.Empty;
                return false;
            }

            parameters.Add(parameterName, filter.Values.Single());
            clause = $"{documentAlias}.workflow_state = @{parameterName}";
            return true;
        }
    }
}
