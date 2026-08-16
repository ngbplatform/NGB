using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportModelsFullCoverageTests
{
    [Fact]
    public void Field_binding_validates_arguments_exposes_metadata_and_resolves_every_time_grain()
    {
        AssertRequired(() => new PostgresReportFieldBinding(" ", "f.value", "string"));
        AssertRequired(() => new PostgresReportFieldBinding("field", "", "string"));
        AssertRequired(() => new PostgresReportFieldBinding("field", "f.value", null!));

        var sut = new PostgresReportFieldBinding(
            " Period ",
            "f.period",
            "date",
            "day(f.period)",
            "week(f.period)",
            "month(f.period)",
            "quarter(f.period)",
            "year(f.period)");

        sut.FieldCodeNorm.Should().Be("period");
        sut.SqlExpression.Should().Be("f.period");
        sut.DataType.Should().Be("date");
        sut.ResolveExpression(null).Should().Be("f.period");
        sut.ResolveExpression(ReportTimeGrain.Day).Should().Be("day(f.period)");
        sut.ResolveExpression(ReportTimeGrain.Week).Should().Be("week(f.period)");
        sut.ResolveExpression(ReportTimeGrain.Month).Should().Be("month(f.period)");
        sut.ResolveExpression(ReportTimeGrain.Quarter).Should().Be("quarter(f.period)");
        sut.ResolveExpression(ReportTimeGrain.Year).Should().Be("year(f.period)");
    }

    [Theory]
    [InlineData(ReportTimeGrain.Day)]
    [InlineData(ReportTimeGrain.Week)]
    [InlineData(ReportTimeGrain.Month)]
    [InlineData(ReportTimeGrain.Quarter)]
    [InlineData(ReportTimeGrain.Year)]
    [InlineData((ReportTimeGrain)int.MaxValue)]
    public void Field_binding_rejects_missing_or_unsupported_time_grains(ReportTimeGrain timeGrain)
    {
        var sut = new PostgresReportFieldBinding("field", "f.value", "date");

        Action act = () => sut.ResolveExpression(timeGrain);

        act.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Measure_binding_validates_arguments_and_resolves_supported_aggregations()
    {
        AssertRequired(() => new PostgresReportMeasureBinding("", "f.amount", "decimal"));
        AssertRequired(() => new PostgresReportMeasureBinding("amount", " ", "decimal"));
        AssertRequired(() => new PostgresReportMeasureBinding("amount", "f.amount", " "));

        var sut = new PostgresReportMeasureBinding(" Amount ", "f.amount", "decimal");
        sut.MeasureCodeNorm.Should().Be("amount");
        sut.SqlExpression.Should().Be("f.amount");
        sut.DataType.Should().Be("decimal");
        sut.ResolveAggregateExpression(ReportAggregationKind.Sum).Should().Be("SUM(f.amount)");
        sut.ResolveAggregateExpression(ReportAggregationKind.Count).Should().Be("COUNT(f.amount)");
        sut.ResolveAggregateExpression(ReportAggregationKind.Min).Should().Be("MIN(f.amount)");
        sut.ResolveAggregateExpression(ReportAggregationKind.Max).Should().Be("MAX(f.amount)");
        sut.ResolveAggregateExpression(ReportAggregationKind.Average).Should().Be("AVG(f.amount)");

        Action unsupported = () => sut.ResolveAggregateExpression(ReportAggregationKind.CountDistinct);
        unsupported.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Dataset_binding_validates_builds_empty_or_complete_maps_and_rejects_duplicates()
    {
        var field = new PostgresReportFieldBinding("field", "f.value", "string");
        var measure = new PostgresReportMeasureBinding("amount", "f.amount", "decimal");
        AssertRequired(() => new PostgresReportDatasetBinding(" ", "fact f", [], []));
        AssertRequired(() => new PostgresReportDatasetBinding("dataset", "", [], []));

        var empty = new PostgresReportDatasetBinding(" Empty ", "fact f", null!, null!, "f.active");
        empty.DatasetCodeNorm.Should().Be("empty");
        empty.FromSql.Should().Be("fact f");
        empty.BaseWhereSql.Should().Be("f.active");
        empty.Fields.Should().BeEmpty();
        empty.Measures.Should().BeEmpty();

        var sut = new PostgresReportDatasetBinding(" Dataset ", "fact f", [field], [measure]);
        sut.GetField(" FIELD ").Should().BeSameAs(field);
        sut.GetMeasure(" AMOUNT ").Should().BeSameAs(measure);
        Action missingField = () => sut.GetField("missing");
        Action missingMeasure = () => sut.GetMeasure("missing");
        missingField.Should().Throw<NgbConfigurationViolationException>();
        missingMeasure.Should().Throw<NgbConfigurationViolationException>();

        Action duplicateField = () => new PostgresReportDatasetBinding(
            "dataset", "fact f", [field, field], []);
        Action duplicateMeasure = () => new PostgresReportDatasetBinding(
            "dataset", "fact f", [], [measure, measure]);
        duplicateField.Should().Throw<NgbConfigurationViolationException>();
        duplicateMeasure.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Dataset_catalog_handles_null_sources_null_results_lookup_and_missing_datasets()
    {
        Action nullSources = () => new PostgresReportDatasetCatalog(null!);
        nullSources.Should().Throw<NgbConfigurationViolationException>();

        var empty = new PostgresReportDatasetCatalog([new StubSource(null!)]);
        Action missingFromEmpty = () => empty.GetDataset("missing");
        missingFromEmpty.Should().Throw<NgbConfigurationViolationException>();

        var binding = new PostgresReportDatasetBinding("dataset", "fact f", [], []);
        var sut = new PostgresReportDatasetCatalog([new StubSource([binding])]);
        sut.GetDataset(" DATASET ").Should().BeSameAs(binding);
        Action missing = () => sut.GetDataset("other");
        missing.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Execution_request_result_and_selection_records_expose_their_complete_contracts()
    {
        Action blankDataset = () => new PostgresReportExecutionRequest(
            " ", [], [], [], [], [], [], new Dictionary<string, object?>(), new PostgresReportPaging(0, 1));
        blankDataset.Should().Throw<NgbArgumentRequiredException>();

        using var json = JsonDocument.Parse("\"active\"");
        var grouping = new PostgresReportGroupingSelection(
            "period", "period__month", "Period", "date", ReportTimeGrain.Month, true, true, true, "group");
        var field = new PostgresReportFieldSelection("name", "name", "Name", "string");
        var measure = new PostgresReportMeasureSelection(
            "amount", "amount__sum", "Amount", "decimal", ReportAggregationKind.Sum, "N2");
        var sort = new PostgresReportSortSelection(
            "period", "amount", ReportSortDirection.Desc, ReportTimeGrain.Month, true, "group");
        var predicate = new PostgresReportPredicateSelection(
            "state", "state", "State", "string", new ReportFilterValueDto(json.RootElement.Clone(), true));
        var paging = new PostgresReportPaging(10, 20, "cursor", true);
        var request = new PostgresReportExecutionRequest(
            " Dataset ", [grouping], [grouping], [field], [measure], [sort], [predicate],
            new Dictionary<string, object?> { ["tenant"] = "one" }, paging);

        request.DatasetCodeNorm.Should().Be("dataset");
        grouping.Should().BeEquivalentTo(new
        {
            FieldCode = "period",
            OutputCode = "period__month",
            Label = "Period",
            DataType = "date",
            TimeGrain = (ReportTimeGrain?)ReportTimeGrain.Month,
            IncludeDetails = true,
            IncludeEmpty = true,
            IncludeDescendants = true,
            GroupKey = "group"
        });
        predicate.Should().BeEquivalentTo(new
        {
            FieldCode = "state",
            OutputCode = "state",
            Label = "State",
            DataType = "string"
        });
        request.Should().BeEquivalentTo(new
        {
            DatasetCode = " Dataset ",
            RowGroups = new[] { grouping },
            ColumnGroups = new[] { grouping },
            DetailFields = new[] { field },
            Measures = new[] { measure },
            Sorts = new[] { sort },
            Predicates = new[] { predicate },
            Paging = paging
        });

        var column = new PostgresReportOutputColumn("amount", "Amount", "decimal", "measure");
        var row = new PostgresReportExecutionRow(new Dictionary<string, object?> { ["amount"] = 12m });
        var result = new PostgresReportExecutionResult(
            [column], [row], 10, 20, true, "next", 31, new Dictionary<string, string> { ["plan"] = "ok" });
        column.OutputCode.Should().Be("amount");
        column.Title.Should().Be("Amount");
        column.DataType.Should().Be("decimal");
        column.SemanticRole.Should().Be("measure");
        result.Should().BeEquivalentTo(new
        {
            Columns = new[] { column },
            Rows = new[] { row },
            Offset = 10,
            Limit = 20,
            HasMore = true,
            NextCursor = "next",
            Total = (int?)31
        });
    }

    private static void AssertRequired(Action act)
        => act.Should().Throw<NgbArgumentRequiredException>();

    private sealed class StubSource(IReadOnlyList<PostgresReportDatasetBinding> datasets)
        : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => datasets;
    }
}
