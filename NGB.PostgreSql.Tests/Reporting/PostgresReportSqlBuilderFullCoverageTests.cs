using System.Reflection;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportSqlBuilderFullCoverageTests
{
    [Fact]
    public void Build_guards_required_input_selection_and_covers_base_where_and_default_order_fallbacks()
    {
        Action nullCatalog = () => new PostgresReportSqlBuilder(null!);
        nullCatalog.Should().Throw<NgbConfigurationViolationException>();

        var sut = Builder(baseWhereSql: "f.active");
        Action nullRequest = () => sut.Build(null!);
        Action emptySelection = () => sut.Build(Request());
        nullRequest.Should().Throw<NgbArgumentRequiredException>();
        emptySelection.Should().Throw<NgbConfigurationViolationException>().WithMessage("*select at least one*");

        var detail = sut.Build(Request(
            details: [new PostgresReportFieldSelection("name", "name", "Name", "string")]));
        detail.Sql.Should().Contain("WHERE (f.active)").And.Contain("ORDER BY name");

        var measure = sut.Build(Request(
            measures:
            [
                new PostgresReportMeasureSelection(
                    "amount", "amount__sum", "Amount", "decimal", ReportAggregationKind.Sum)
            ]));
        measure.Sql.Should().Contain("ORDER BY amount__sum").And.NotContain("GROUP BY");

        var accountWithoutId = Builder(
            fields: [new PostgresReportFieldBinding("account_display", "f.account_display", "string")]);
        accountWithoutId.Build(Request(details:
        [
            new PostgresReportFieldSelection("account_display", "account_display", "Account", "string")
        ])).Columns.Should().ContainSingle();
    }

    [Fact]
    public void Build_bounds_legacy_offset_before_it_reaches_postgresql()
    {
        var sut = Builder();
        var request = Request(details:
        [
            new PostgresReportFieldSelection("name", "name", "Name", "string")
        ]) with
        {
            Paging = new PostgresReportPaging(int.MaxValue, 20)
        };

        var statement = sut.Build(request);

        statement.Offset.Should().Be(PagingLimits.MaxOffset);
        statement.Parameters.Get<int>("offset").Should().Be(PagingLimits.MaxOffset);
    }

    [Fact]
    public void Json_conversion_and_predicate_helpers_cover_every_supported_shape_and_fallback()
    {
        Convert(null).Should().BeNull();
        Convert("\"00000000-0000-0000-0000-000000000123\"").Should().BeOfType<Guid>();
        Convert("\"2026-08-16T12:30:00+00:00\"").Should().BeOfType<DateTimeOffset>();
        Convert("\"text\"").Should().Be("text");
        Convert("true").Should().Be(true);
        Convert("false").Should().Be(false);
        Convert("17").Should().Be(17L);
        Convert("17.25").Should().Be(17.25m);
        Convert("1e100").Should().BeOfType<double>();
        Convert("{\"nested\":true}").Should().Be("{\"nested\":true}");

        ConvertArray("[]").Should().BeOfType<string[]>().Which.Should().BeEmpty();
        ConvertArray("[\"00000000-0000-0000-0000-000000000123\"]").Should().BeOfType<Guid[]>();
        ConvertArray("[\"one\",null]").Should().BeOfType<string?[]>();
        ConvertArray("[1,2]").Should().BeOfType<long[]>();
        ConvertArray("[1.25,2.5]").Should().BeOfType<decimal[]>();
        ConvertArray("[1e100,2e100]").Should().BeOfType<double[]>();
        ConvertArray("[true,1]").Should().BeOfType<object[]>();

        var parameters = new DynamicParameters();
        Invoke<string>("BuildPredicateSql", "f.value", "p_null", Filter("null"), parameters)
            .Should().Be("f.value IS NULL");
        Invoke<string>("BuildPredicateSql", "f.value", "p_array", Filter("[1,2]"), parameters)
            .Should().Be("f.value = ANY(@p_array)");
        Invoke<string>("BuildPredicateSql", "f.value", "p_scalar", Filter("\"value\""), parameters)
            .Should().Be("f.value = @p_scalar");
        parameters.ParameterNames.Should().BeEquivalentTo("p_array", "p_scalar");

        Invoke<string>("BuildWhereClause", (object)new[] { "a = 1", "b = 2" })
            .Should().Be("WHERE a = 1 AND b = 2");
        Invoke<string>("BuildWhereClause", (object)Array.Empty<string>()).Should().BeEmpty();
        Invoke<string>("BuildGroupByClause", new[] { "a" }, false).Should().BeEmpty();
        Invoke<string>("BuildGroupByClause", Array.Empty<string>(), true).Should().BeEmpty();
        Invoke<string>("BuildGroupByClause", new[] { "a", "b" }, true).Should().Be("GROUP BY a,b");
    }

    [Fact]
    public void Sort_resolution_covers_measure_group_key_axis_field_detail_and_failures()
    {
        var row = new PostgresReportGroupingSelection(
            "period", "period__month", "Period", "date", ReportTimeGrain.Month, GroupKey: "row-key");
        var column = new PostgresReportGroupingSelection(
            "state", "state", "State", "string", GroupKey: "column-key");
        var detail = new PostgresReportFieldSelection("name", "name", "Name", "string");
        var measure = new PostgresReportMeasureSelection(
            "amount", "amount__sum", "Amount", "decimal", ReportAggregationKind.Sum);
        var request = Request(rows: [row], columns: [column], details: [detail], measures: [measure]);

        ResolveSort(request, new PostgresReportSortSelection("ignored", "amount", ReportSortDirection.Desc))
            .Should().Be("amount__sum");
        ResolveSort(request, new PostgresReportSortSelection(
            "ignored", null, ReportSortDirection.Asc, GroupKey: "row-key")).Should().Be("period__month");
        ResolveSort(request, new PostgresReportSortSelection(
            "state", null, ReportSortDirection.Asc, AppliesToColumnAxis: true)).Should().Be("state");
        ResolveSort(request, new PostgresReportSortSelection("name", null, ReportSortDirection.Asc))
            .Should().Be("name");

        AssertInvocationThrows<NgbConfigurationViolationException>(
            "ResolveSortAlias",
            request,
            new PostgresReportSortSelection("ignored", "missing", ReportSortDirection.Asc));
        AssertInvocationThrows<NgbConfigurationViolationException>(
            "ResolveSortAlias",
            request,
            new PostgresReportSortSelection("missing", null, ReportSortDirection.Asc));
        AssertInvocationThrows<NgbConfigurationViolationException>(
            "ResolveSortAlias",
            request,
            new PostgresReportSortSelection(
                "name", null, ReportSortDirection.Asc, AppliesToColumnAxis: true));
    }

    [Fact]
    public void Interactive_support_helpers_cover_all_axes_special_names_missing_ids_and_already_selected_ids()
    {
        var rows = Request(rows: [Group("target")]);
        var columns = Request(columns: [Group("target")]);
        var details = Request(details: [new PostgresReportFieldSelection("target", "target", "Target", "string")]);
        var none = Request(details: [new PostgresReportFieldSelection("other", "other", "Other", "string")]);
        Invoke<bool>("ShouldIncludeSupportField", rows, "target").Should().BeTrue();
        Invoke<bool>("ShouldIncludeSupportField", columns, "target").Should().BeTrue();
        Invoke<bool>("ShouldIncludeSupportField", details, "target").Should().BeTrue();
        Invoke<bool>("ShouldIncludeSupportField", none, "target").Should().BeFalse();
        Invoke<bool>("IsFieldSelected", rows, "target").Should().BeTrue();
        Invoke<bool>("IsFieldSelected", columns, "target").Should().BeTrue();
        Invoke<bool>("IsFieldSelected", details, "target").Should().BeTrue();
        Invoke<bool>("IsFieldSelected", none, "target").Should().BeFalse();

        var dataset = Dataset(fields:
        [
            Field("name"),
            Field("account_display"),
            Field("document_display"),
            Field("missing_display"),
            Field("item_display"),
            Field("item_id")
        ]);
        var selection = Request(rows:
        [
            Group("name"),
            Group("account_display"),
            Group("document_display"),
            Group("missing_display"),
            Group("item_display")
        ]);
        Invoke<IReadOnlyList<string>>("ResolveCatalogSupportFieldCodes", selection, dataset)
            .Should().Equal("item_id");

        var alreadySelected = Request(rows: [Group("item_display"), Group("item_id")]);
        Invoke<IReadOnlyList<string>>("ResolveCatalogSupportFieldCodes", alreadySelected, dataset)
            .Should().BeEmpty();
    }

    private static PostgresReportSqlBuilder Builder(
        string? baseWhereSql = null,
        IReadOnlyList<PostgresReportFieldBinding>? fields = null)
        => new(new PostgresReportDatasetCatalog([new StubSource(Dataset(baseWhereSql, fields))]));

    private static PostgresReportDatasetBinding Dataset(
        string? baseWhereSql = null,
        IReadOnlyList<PostgresReportFieldBinding>? fields = null)
        => new(
            "dataset",
            "fact f",
            fields ??
            [
                Field("name"),
                new PostgresReportFieldBinding(
                    "period", "f.period", "date",
                    monthBucketSqlExpression: "month(f.period)"),
                Field("state")
            ],
            [new PostgresReportMeasureBinding("amount", "f.amount", "decimal")],
            baseWhereSql);

    private static PostgresReportFieldBinding Field(string code)
        => new(code, $"f.{code}", code.EndsWith("_id", StringComparison.Ordinal) ? "uuid" : "string");

    private static PostgresReportGroupingSelection Group(string code)
        => new(code, code, code, "string");

    private static PostgresReportExecutionRequest Request(
        IReadOnlyList<PostgresReportGroupingSelection>? rows = null,
        IReadOnlyList<PostgresReportGroupingSelection>? columns = null,
        IReadOnlyList<PostgresReportFieldSelection>? details = null,
        IReadOnlyList<PostgresReportMeasureSelection>? measures = null,
        IReadOnlyList<PostgresReportSortSelection>? sorts = null,
        IReadOnlyList<PostgresReportPredicateSelection>? predicates = null)
        => new(
            "dataset",
            rows ?? [],
            columns ?? [],
            details ?? [],
            measures ?? [],
            sorts ?? [],
            predicates ?? [],
            new Dictionary<string, object?>(),
            new PostgresReportPaging(0, 20));

    private static object? Convert(string? json)
        => Invoke<object?>("ConvertJsonElement", Element(json ?? "null"));

    private static Array ConvertArray(string json)
        => Invoke<Array>("ConvertJsonArray", Element(json));

    private static ReportFilterValueDto Filter(string json) => new(Element(json));

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ResolveSort(PostgresReportExecutionRequest request, PostgresReportSortSelection sort)
        => Invoke<string>("ResolveSortAlias", request, sort);

    private static T Invoke<T>(string name, params object?[] arguments)
        => (T)Method(name).Invoke(null, arguments)!;

    private static void AssertInvocationThrows<TException>(string name, params object?[] arguments)
        where TException : Exception
    {
        Action act = () => Method(name).Invoke(null, arguments);
        act.Should().Throw<TargetInvocationException>().WithInnerException<TException>();
    }

    private static MethodInfo Method(string name)
        => typeof(PostgresReportSqlBuilder).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new MissingMethodException(typeof(PostgresReportSqlBuilder).FullName, name);

    private sealed class StubSource(PostgresReportDatasetBinding dataset) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [dataset];
    }
}
