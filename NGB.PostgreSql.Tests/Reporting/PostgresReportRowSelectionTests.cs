using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportRowSelectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(ReportLayoutLimits.MaxDetailFields)]
    [InlineData(ReportRowSelectionLimits.MaxFields)]
    public void Builder_accepts_the_complete_supported_key_width_with_a_bounded_parameter_count(int fields)
    {
        var keys = ReportRowSelectionLimits.GetMaxKeyCount(fields);
        var statement = Builder(fields).Build(Request(Selection(fields, keys)));
        statement.Parameters.ParameterNames.Count().Should().Be(fields * keys + 1, "the only extra parameter is the page lookahead limit");
        statement.Parameters.ParameterNames.Count().Should().BeLessThanOrEqualTo(ReportRowSelectionLimits.MaxValues + 1);
        statement.Sql.Should().NotContain("v0-secret");
        statement.Parameters.Get<string>("p_0").Should().Be("v0-secret");

        Action excess = () => Builder(fields).Build(Request(Selection(fields, keys + 1)));
        excess.Should().Throw<NgbArgumentInvalidException>().WithMessage("*selection*");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(ReportRowSelectionLimits.MaxFields + 1, 1)]
    [InlineData(1, PagingLimits.MaxPageSize + 1)]
    public void Builder_rejects_unsupported_selection_dimensions(int fields, int keys)
    {
        Action action = () => Builder(Math.Max(fields, 1)).Build(Request(Selection(fields, keys)));
        action.Should().Throw<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Composite_values_cannot_bypass_the_scalar_parameter_budget(string json)
    {
        var selection = new ReportRowSelection([new("f0")], [[JsonDocument.Parse(json).RootElement.Clone()]]);
        Action action = () => Builder(1).Build(Request(selection));
        action.Should().Throw<NgbArgumentInvalidException>().WithMessage("*scalar*");
    }

    [Fact]
    public void Empty_sets_null_keys_and_mismatched_keys_have_explicit_semantics()
    {
        var builder = Builder(1);
        builder.Build(Request(Selection(1, 0))).Sql.Should().Contain("WHERE FALSE");
        var nullSelection = new ReportRowSelection([new("f0")], [[JsonSerializer.SerializeToElement<string?>(null)]]);
        var nullStatement = builder.Build(Request(nullSelection));
        nullStatement.Sql.Should().Contain("f.f0 IS NULL");
        nullStatement.Parameters.ParameterNames.Should().ContainSingle();

        Action missingValue = () => builder.Build(Request(new([new("f0")], [[]])));
        Action undefinedValue = () => builder.Build(Request(new([new("f0")], [[default(JsonElement)]])));
        missingValue.Should().Throw<NgbArgumentInvalidException>().WithMessage("*does not match*");
        undefinedValue.Should().Throw<NgbArgumentInvalidException>().WithMessage("*scalar*");
    }

    [Fact]
    public void Selection_uses_the_requested_time_bucket()
    {
        var binding = new PostgresReportDatasetBinding("selected", "facts f",
            [new("f0", "f.period", "date", monthBucketSqlExpression: "date_trunc('month', f.period)")], []);
        var builder = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([new Source(binding)]));
        var request = Request(new([new("f0", ReportTimeGrain.Month)], [[JsonSerializer.SerializeToElement("2026-09-01T00:00:00Z")]]));
        builder.Build(request).Sql.Should().Contain("date_trunc('month', f.period) = @p_0");
    }

    [Fact]
    public void Streaming_has_no_materialization_or_page_limit_and_rejects_a_missing_request()
    {
        var builder = Builder(1);
        var statement = builder.BuildStreaming(Request(null));
        statement.Sql.Should().NotContain("LIMIT");
        statement.Parameters.ParameterNames.Should().BeEmpty();
        Action action = () => builder.BuildStreaming(null!);
        action.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Final_statement_guards_count_utf8_bytes_and_all_parameters()
    {
        PostgresReportSqlLimits.Validate(new string('a', PostgresReportSqlLimits.MaxStatementTextBytes), PostgresReportSqlLimits.MaxBindParameters);
        Action text = () => PostgresReportSqlLimits.Validate(new string('я', PostgresReportSqlLimits.MaxStatementTextBytes / 2 + 1), 0);
        Action parameters = () => PostgresReportSqlLimits.Validate("SELECT 1", PostgresReportSqlLimits.MaxBindParameters + 1);
        text.Should().Throw<NgbArgumentInvalidException>().WithMessage("*SQL text*");
        parameters.Should().Throw<NgbArgumentInvalidException>().WithMessage("*SQL parameters*");
    }

    [Fact]
    public void Continuation_sql_grows_linearly_with_key_width()
    {
        const int narrowWidth = ReportLayoutLimits.MaxRowGroups;
        const int wideWidth = ReportLayoutLimits.MaxDetailFields;
        var narrow = Continuation(narrowWidth);
        var wide = Continuation(wideWidth);
        wide.Sql.Length.Should().BeLessThan(narrow.Sql.Length * (wideWidth / narrowWidth + 1),
            "wider aliases may add text, but shared key prefixes must not be repeated quadratically");
        wide.Parameters.ParameterNames.Should().HaveCount(wideWidth + 1);

        static PostgresReportSqlStatement Continuation(int fields)
        {
            var builder = Builder(fields);
            var request = Request(null) with
            {
                DetailFields = Enumerable.Range(0, fields).Select(i => new PostgresReportFieldSelection($"f{i}", $"f{i}", $"Field {i}", "string")).ToArray(),
                DistinctGroups = true
            };
            var first = builder.Build(request);
            var cursor = PostgresReportCursorCodec.Encode(first.DatasetCode, first.CursorColumns,
                first.CursorColumns.ToDictionary(column => column.Alias, _ => (object?)"value"));
            return builder.Build(request with { Paging = request.Paging with { Cursor = cursor } });
        }
    }

    private static ReportRowSelection Selection(int fields, int keys) => new(
        Enumerable.Range(0, fields).Select(i => new ReportSelectionField($"f{i}")).ToArray(),
        Enumerable.Range(0, keys).Select(row => (IReadOnlyList<JsonElement>)Enumerable.Range(0, fields)
            .Select(_ => JsonSerializer.SerializeToElement($"v{row}-secret")).ToArray()).ToArray());

    private static PostgresReportSqlBuilder Builder(int fields) => new(new PostgresReportDatasetCatalog([new Source(
        new("selected", "facts f", Enumerable.Range(0, fields).Select(i => new PostgresReportFieldBinding($"f{i}", $"f.f{i}", "string")).ToArray(), []))]));

    private static PostgresReportExecutionRequest Request(ReportRowSelection? selection) => new(
        "selected", [], [], [new("f0", "f0", "Field", "string")], [], [], [],
        new Dictionary<string, object?>(), new(0, PagingLimits.MaxPageSize), Selection: selection);

    private sealed class Source(PostgresReportDatasetBinding binding) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [binding];
    }
}
