using System.Data;
using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresLedgerAnalysisFlatDetailReaderFullCoverageTests
{
    private static readonly DateTime FromUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_rejects_missing_dependencies()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection);
        var catalog = Catalog();

        Action missingUow = () => _ = new PostgresLedgerAnalysisFlatDetailReader(null!, catalog);
        Action missingCatalog = () => _ = new PostgresLedgerAnalysisFlatDetailReader(uow, null!);

        missingUow.Should().Throw<NgbConfigurationViolationException>();
        missingCatalog.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task GetPage_rejects_null_request_and_non_positive_paged_size()
    {
        var (sut, _) = Fixture();

        Func<Task> nullRequest = () => sut.GetPageAsync(null!);
        Func<Task> zeroSize = () => sut.GetPageAsync(Request(pageSize: 0));
        Func<Task> negativeSize = () => sut.GetPageAsync(Request(pageSize: -1));

        await nullRequest.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroSize.Should().ThrowAsync<NgbArgumentInvalidException>();
        await negativeSize.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task GetPage_builds_full_query_materializes_rows_and_adds_interactive_support()
    {
        var row = new Dictionary<string, object?>
        {
            ["__cursor_period_utc"] = FromUtc,
            ["__cursor_entry_id"] = 10L,
            ["__cursor_posting_side"] = "debit",
            ["account_name"] = "1000 · Cash",
            ["document_name"] = "GJE-1",
            ["amount_out"] = 42m,
            ["__support_account_id"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ["__support_document_id"] = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        var (sut, connection) = Fixture([row]);
        var predicates = new[]
        {
            Predicate("filter_value", "null", "null"),
            Predicate("filter_value", "array", "[1,2]"),
            Predicate("filter_value", "scalar", "true")
        };
        var request = Request(
            details:
            [
                new("account_display", "account_name", "Account", "string"),
                new("document_display", "document_name", "Document", "string")
            ],
            measures: [new("amount", "amount_out", "Amount", "decimal")],
            predicates: predicates,
            pageSize: 0,
            disablePaging: true);

        var page = await sut.GetPageAsync(request);

        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        page.Rows.Should().ContainSingle();
        page.Rows[0].Values.Should().ContainKey("account_name");
        page.Rows[0].Values.Should().ContainKey("__support_account_id");
        page.Rows[0].Values.Should().NotContainKey("__cursor_period_utc");
        var command = connection.Commands.Should().ContainSingle().Subject;
        command.CommandText.Should().Contain("(e.is_active)");
        command.CommandText.Should().Contain("e.filter_value IS NULL");
        command.CommandText.Should().Contain("e.filter_value = ANY(");
        command.CommandText.Should().Contain("e.filter_value = @p_2");
        command.CommandText.Should().Contain("AS __support_account_id");
        command.CommandText.Should().Contain("AS __support_document_id");
        command.CommandText.Should().NotContain("LIMIT @limit_plus_one");
    }

    [Fact]
    public async Task GetPage_omits_unavailable_interactive_support_and_empty_where_clause()
    {
        var (sut, connection) = Fixture(dataset: Dataset(includeSupportFields: false, baseWhereSql: null));
        var request = Request(
            details:
            [
                new("account_display", "account_name", "Account", "string"),
                new("document_display", "document_name", "Document", "string")
            ],
            disablePaging: true);

        var page = await sut.GetPageAsync(request);

        page.Rows.Should().BeEmpty();
        var sql = connection.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().NotContain("AS __support_account_id");
        sql.Should().NotContain("AS __support_document_id");
        sql.Should().NotContain("WHERE");
    }

    [Fact]
    public async Task GetPage_applies_cursor_paging_and_returns_next_cursor()
    {
        var first = new Dictionary<string, object?>
        {
            ["__cursor_period_utc"] = FromUtc,
            ["__cursor_entry_id"] = 10L,
            ["__cursor_posting_side"] = "credit",
            ["property_out"] = "Property A"
        };
        var second = new Dictionary<string, object?>
        {
            ["__cursor_period_utc"] = FromUtc.AddDays(1),
            ["__cursor_entry_id"] = 11L,
            ["__cursor_posting_side"] = "debit",
            ["property_out"] = "Property B"
        };
        var (sut, connection) = Fixture([first, second]);
        var request = Request(
            details: [new("property", "property_out", "Property", "string")],
            pageSize: 1,
            cursor: new(FromUtc.AddDays(-1), 5, "debit"));

        var page = await sut.GetPageAsync(request);

        page.HasMore.Should().BeTrue();
        page.Rows.Should().ContainSingle();
        page.Rows[0].Values["property_out"].Should().Be("Property A");
        page.NextCursor.Should().Be(new LedgerAnalysisFlatDetailCursor(FromUtc, 10, "credit"));
        var command = connection.Commands.Should().ContainSingle().Subject;
        command.CommandText.Should().Contain("@after_period_utc");
        command.CommandText.Should().Contain("LIMIT @limit_plus_one");
        command.ParametersSnapshot.Should().Contain(parameter => parameter.ParameterName == "after_entry_id");
    }

    [Fact]
    public async Task GetPage_rejects_duplicate_and_unsafe_output_aliases()
    {
        var (sut, _) = Fixture();
        var duplicateRequest = Request(
            details: [new("property", "result", "Property", "string")],
            measures: [new("amount", "result", "Amount", "decimal")],
            disablePaging: true);
        var reservedRequest = Request(
            details: [new("property", "__cursor_entry_id", "Property", "string")],
            disablePaging: true);
        var unsafeRequest = Request(
            details: [new("property", "bad-alias", "Property", "string")],
            disablePaging: true);

        Func<Task> duplicate = () => sut.GetPageAsync(duplicateRequest);
        Func<Task> reserved = () => sut.GetPageAsync(reservedRequest);
        Func<Task> unsafeAlias = () => sut.GetPageAsync(unsafeRequest);

        await duplicate.Should().ThrowAsync<NgbInvariantViolationException>();
        await reserved.Should().ThrowAsync<NgbInvariantViolationException>();
        await unsafeAlias.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Json_array_conversion_covers_empty_homogeneous_and_mixed_boundaries()
    {
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[]"))
            .Should().BeOfType<string[]>().Which.Should().BeEmpty();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[\"11111111-1111-1111-1111-111111111111\"]"))
            .Should().BeOfType<Guid[]>();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[\"alpha\",null]"))
            .Should().BeOfType<string?[]>();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[1,2]"))
            .Should().BeOfType<long[]>();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[1.25,2.5]"))
            .Should().BeOfType<decimal[]>();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[1e100,2e100]"))
            .Should().BeOfType<double[]>();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonArray(Json("[true,1]"))
            .Should().BeOfType<object[]>();
    }

    [Fact]
    public void Json_element_conversion_covers_every_supported_kind_and_fallback()
    {
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("null")).Should().BeNull();
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("\"11111111-1111-1111-1111-111111111111\""))
            .Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("\"2026-08-16T12:00:00+02:00\""))
            .Should().Be(DateTimeOffset.Parse("2026-08-16T12:00:00+02:00"));
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("\"text\"")).Should().Be("text");
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("true")).Should().Be(true);
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("false")).Should().Be(false);
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("42")).Should().Be(42L);
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("1.25")).Should().Be(1.25m);
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("1e100")).Should().Be(1e100d);
        PostgresLedgerAnalysisFlatDetailReader.ConvertJsonElement(Json("{\"a\":1}")).Should().Be("{\"a\":1}");
    }

    [Fact]
    public void Cursor_timestamp_conversion_accepts_supported_values_and_rejects_missing_or_invalid_values()
    {
        var unspecified = DateTime.SpecifyKind(FromUtc, DateTimeKind.Unspecified);
        PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value(FromUtc), "value").Should().Be(FromUtc);
        PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value(unspecified), "value").Kind.Should().Be(DateTimeKind.Utc);
        PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value(new DateTimeOffset(FromUtc)), "value").Should().Be(FromUtc);
        PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value("2026-08-01T00:00:00Z"), "value").Should().Be(FromUtc);

        Action missing = () => PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(new Dictionary<string, object?>(), "value");
        Action nullValue = () => PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value(null), "value");
        Action invalid = () => PostgresLedgerAnalysisFlatDetailReader.ReadDateTimeUtc(Value(42), "value");
        missing.Should().Throw<NgbInvariantViolationException>();
        nullValue.Should().Throw<NgbInvariantViolationException>();
        invalid.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public void Cursor_integer_conversion_accepts_supported_values_and_rejects_missing_or_invalid_values()
    {
        PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value(42L), "value").Should().Be(42);
        PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value(42), "value").Should().Be(42);
        PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value(42m), "value").Should().Be(42);
        PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value("42"), "value").Should().Be(42);

        Action missing = () => PostgresLedgerAnalysisFlatDetailReader.ReadInt64(new Dictionary<string, object?>(), "value");
        Action nullValue = () => PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value(null), "value");
        Action invalidText = () => PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value("forty-two"), "value");
        Action invalidType = () => PostgresLedgerAnalysisFlatDetailReader.ReadInt64(Value(true), "value");
        missing.Should().Throw<NgbInvariantViolationException>();
        nullValue.Should().Throw<NgbInvariantViolationException>();
        invalidText.Should().Throw<NgbInvariantViolationException>();
        invalidType.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public void Cursor_string_conversion_and_row_materialization_cover_success_and_failure_paths()
    {
        PostgresLedgerAnalysisFlatDetailReader.ReadString(Value("debit"), "value").Should().Be("debit");

        Action missing = () => PostgresLedgerAnalysisFlatDetailReader.ReadString(new Dictionary<string, object?>(), "value");
        Action nullValue = () => PostgresLedgerAnalysisFlatDetailReader.ReadString(Value(null), "value");
        Action whitespace = () => PostgresLedgerAnalysisFlatDetailReader.ReadString(Value("  "), "value");
        Action invalidType = () => PostgresLedgerAnalysisFlatDetailReader.ReadString(Value(1), "value");
        missing.Should().Throw<NgbInvariantViolationException>();
        nullValue.Should().Throw<NgbInvariantViolationException>();
        whitespace.Should().Throw<NgbInvariantViolationException>();
        invalidType.Should().Throw<NgbInvariantViolationException>();

        var materialized = PostgresLedgerAnalysisFlatDetailReader.MaterializeRow(
            new Dictionary<string, object?> { ["VALUE"] = 42 });
        materialized["value"].Should().Be(42);
        Action invalidRow = () => PostgresLedgerAnalysisFlatDetailReader.MaterializeRow(new object());
        invalidRow.Should().Throw<NgbInvariantViolationException>();
    }

    private static LedgerAnalysisFlatDetailPredicate Predicate(
        string fieldCode,
        string outputCode,
        string json)
        => new(fieldCode, outputCode, outputCode, "string", Json(json));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyDictionary<string, object?> Value(object? value)
        => new Dictionary<string, object?> { ["value"] = value };

    private static LedgerAnalysisFlatDetailPageRequest Request(
        IReadOnlyList<LedgerAnalysisFlatDetailFieldSelection>? details = null,
        IReadOnlyList<LedgerAnalysisFlatDetailMeasureSelection>? measures = null,
        IReadOnlyList<LedgerAnalysisFlatDetailPredicate>? predicates = null,
        int pageSize = 25,
        LedgerAnalysisFlatDetailCursor? cursor = null,
        bool disablePaging = false)
        => new(
            "ledger",
            details ?? [],
            measures ?? [],
            predicates ?? [],
            FromUtc,
            ToUtc,
            pageSize,
            cursor,
            disablePaging);

    private static (PostgresLedgerAnalysisFlatDetailReader Reader, RecordingDbConnection Connection) Fixture(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null,
        PostgresReportDatasetBinding? dataset = null)
    {
        var connection = new RecordingDbConnection(readerFactory: _ => Rows(rows ?? []));
        var reader = new PostgresLedgerAnalysisFlatDetailReader(
            new RecordingUnitOfWork(connection),
            Catalog(dataset));
        return (reader, connection);
    }

    private static PostgresReportDatasetCatalog Catalog(PostgresReportDatasetBinding? dataset = null)
        => new([new DatasetSource(dataset ?? Dataset())]);

    private static PostgresReportDatasetBinding Dataset(
        bool includeSupportFields = true,
        string? baseWhereSql = "e.is_active")
    {
        var fields = new List<PostgresReportFieldBinding>
        {
            new("period_utc", "e.period_utc", "datetime"),
            new("entry_id", "e.entry_id", "long"),
            new("posting_side", "e.posting_side", "string"),
            new("property", "e.property", "string"),
            new("account_display", "e.account_display", "string"),
            new("document_display", "e.document_display", "string"),
            new("filter_value", "e.filter_value", "string")
        };
        if (includeSupportFields)
        {
            fields.Add(new("account_id", "e.account_id", "guid"));
            fields.Add(new("document_id", "e.document_id", "guid"));
        }

        return new PostgresReportDatasetBinding(
            "ledger",
            "ledger_entries e",
            fields,
            [new PostgresReportMeasureBinding("amount", "e.amount", "decimal")],
            baseWhereSql);
    }

    private static System.Data.Common.DbDataReader Rows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        foreach (var column in rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(column, typeof(object));

        foreach (var values in rows)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
                row[column] = values.TryGetValue(column.ColumnName, out var value) && value is not null
                    ? value
                    : DBNull.Value;
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }

    private sealed class DatasetSource(PostgresReportDatasetBinding dataset) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [dataset];
    }
}
