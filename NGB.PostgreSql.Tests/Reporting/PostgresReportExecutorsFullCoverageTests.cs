using System.Data;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportExecutorsFullCoverageTests
{
    [Fact]
    public async Task Constructors_and_execution_reject_missing_required_dependencies_and_arguments()
    {
        var source = new StubSource([Dataset()]);
        var catalog = new PostgresReportDatasetCatalog([source]);
        var builder = new PostgresReportSqlBuilder(catalog);
        var uow = new RecordingUnitOfWork(new RecordingDbConnection());

        Action missingUow = () => new PostgresReportDatasetExecutor(null!, builder);
        Action missingBuilder = () => new PostgresReportDatasetExecutor(uow, null!);
        Action missingExecutor = () => new PostgresReportPlanExecutor(null!);
        missingUow.Should().Throw<NgbConfigurationViolationException>();
        missingBuilder.Should().Throw<NgbConfigurationViolationException>();
        missingExecutor.Should().Throw<NgbConfigurationViolationException>();

        var datasetExecutor = new PostgresReportDatasetExecutor(uow, builder);
        var planExecutor = new PostgresReportPlanExecutor(datasetExecutor);
        Func<Task> missingDatasetRequest = () => datasetExecutor.ExecuteAsync(null!, default);
        Func<Task> missingDefinition = () => Execute(planExecutor, null!, new(), "dataset", [], [], [], [], [], [], [], new(0, 1));
        Func<Task> missingRequest = () => Execute(planExecutor, new("report", "Report"), null!, "dataset", [], [], [], [], [], [], [], new(0, 1));
        Func<Task> missingDatasetCode = () => Execute(planExecutor, new("report", "Report"), new(), null, [], [], [], [], [], [], [], new(0, 1));

        await missingDatasetRequest.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingDefinition.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingRequest.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingDatasetCode.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Plan_executor_maps_complete_plan_strict_dates_and_paged_result()
    {
        var data = new DataTable();
        data.Columns.Add("period_out", typeof(DateTime));
        data.Columns.Add("category_out", typeof(string));
        data.Columns.Add("name_out", typeof(string));
        data.Columns.Add("amount_out", typeof(decimal));
        data.Rows.Add(new DateTime(2026, 8, 1), "A", "First", 10m);
        data.Rows.Add(new DateTime(2026, 8, 2), "B", "Second", 20m);
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = CreatePlanExecutor(connection);
        using var filterJson = System.Text.Json.JsonDocument.Parse("\"active\"");

        var page = await Execute(
            sut,
            new ReportDefinitionDto("report", "Report"),
            new ReportExecutionRequestDto(DisablePaging: false),
            "dataset",
            [new("period", "period_out", "Period", "date", ReportTimeGrain.Month, IncludeDetails: true, IncludeEmpty: true, IncludeDescendants: true, GroupKey: "row")],
            [new("category", "category_out", "Category", "string", IsColumnAxis: true, GroupKey: "column")],
            [new("name", "name_out", "Name", "string")],
            [new("amount", "amount_out", "Amount", "decimal", ReportAggregationKind.Sum, "N2")],
            [new("period", "amount", ReportSortDirection.Desc, ReportTimeGrain.Month, AppliesToColumnAxis: true, GroupKey: "sort")],
            [new("state", "state_out", "State", "string", new(filterJson.RootElement.Clone(), true))],
            [
                new("from_utc", "2026-08-01"),
                new("to_utc", "2026-08-16"),
                new("as_of_utc", "2026-08-15"),
                new("tenant", "north")
            ],
            new(0, 1));

        page.Columns.Should().HaveCount(4);
        page.Columns.Select(x => x.Code).Should().Equal("period_out", "category_out", "name_out", "amount_out");
        page.Rows.Should().ContainSingle();
        page.Rows[0].Values.Should().Contain("NAME_OUT", "First");
        page.Offset.Should().Be(0);
        page.Limit.Should().Be(1);
        page.Total.Should().BeNull();
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().NotBeNullOrWhiteSpace();
        page.Diagnostics.Should().Contain("executor", "postgres-foundation")
            .And.Contain("aggregated", "True")
            .And.Contain("rowCount", "1");
        connection.State.Should().Be(ConnectionState.Open);

        var parameters = connection.Commands.Single().ParametersSnapshot
            .ToDictionary(x => x.ParameterName, x => x.Value, StringComparer.OrdinalIgnoreCase);
        parameters["from_utc"].Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        parameters["to_utc"].Should().Be(new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc));
        parameters["to_utc_exclusive"].Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
        parameters["as_of_utc_exclusive"].Should().Be(new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc));
        parameters["tenant"].Should().Be("north");
    }

    [Fact]
    public async Task Plan_executor_maps_unpaged_result_and_rejects_non_iso_dates()
    {
        var data = new DataTable();
        data.Columns.Add("name_out", typeof(string));
        data.Rows.Add("Only");
        var connection = new RecordingDbConnection(_ => data.CreateDataReader());
        var sut = CreatePlanExecutor(connection);

        var page = await Execute(
            sut,
            new("report", "Report"),
            new(DisablePaging: true),
            "dataset",
            [],
            [],
            [new("name", "name_out", "Name", "string")],
            [],
            [],
            [],
            [new("tenant", "north")],
            new(99, 10));

        page.Offset.Should().Be(0);
        page.Limit.Should().Be(1);
        page.Total.Should().Be(1);
        page.HasMore.Should().BeFalse();

        foreach (var invalid in new[] { "08/16/2026", "2026-02-30", "not-a-date" })
        {
            Func<Task> act = () => Execute(
                sut, new("report", "Report"), new(), "dataset", [], [],
                [new("name", "name_out", "Name", "string")], [], [], [],
                [new("from_utc", invalid)], new(0, 10));
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }

    [Fact]
    public void Row_materialization_is_case_insensitive_and_rejects_non_dictionary_payloads()
    {
        var values = PostgresReportDatasetExecutor.MaterializeRow(
            new Dictionary<string, object?> { ["Value"] = 42 });
        values.Should().Contain("value", 42);

        Action invalid = () => PostgresReportDatasetExecutor.MaterializeRow(42);
        invalid.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public void Unpaged_materialization_guard_accepts_boundary_and_rejects_one_extra_row()
    {
        Action boundary = () => PostgresReportDatasetExecutor.EnsureMaterializationBound(
            Contracts.Common.PagingLimits.MaxMaterializedRows);
        Action exceeded = () => PostgresReportDatasetExecutor.EnsureMaterializationBound(
            Contracts.Common.PagingLimits.MaxMaterializedRows + 1);

        boundary.Should().NotThrow();
        exceeded.Should().Throw<NgbArgumentOutOfRangeException>()
            .WithMessage("*materialize at most*");
    }

    private static PostgresReportPlanExecutor CreatePlanExecutor(RecordingDbConnection connection)
    {
        var catalog = new PostgresReportDatasetCatalog([new StubSource([Dataset()])]);
        var datasetExecutor = new PostgresReportDatasetExecutor(
            new RecordingUnitOfWork(connection),
            new PostgresReportSqlBuilder(catalog));
        return new PostgresReportPlanExecutor(datasetExecutor);
    }

    private static PostgresReportDatasetBinding Dataset()
        => new(
            "dataset",
            "reporting_rows r",
            [
                new("period", "r.period", "date", monthBucketSqlExpression: "date_trunc('month', r.period)"),
                new("category", "r.category", "string"),
                new("name", "r.name", "string"),
                new("state", "r.state", "string")
            ],
            [new("amount", "r.amount", "decimal")]);

    private static Task<ReportDataPage> Execute(
        PostgresReportPlanExecutor sut,
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string? datasetCode,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<ReportPlanFieldSelection> detailFields,
        IReadOnlyList<ReportPlanMeasure> measures,
        IReadOnlyList<ReportPlanSort> sorts,
        IReadOnlyList<ReportPlanPredicate> predicates,
        IReadOnlyList<ReportPlanParameter> parameters,
        ReportPlanPaging paging)
        => sut.ExecuteAsync(
            definition,
            request,
            "report",
            datasetCode,
            rowGroups,
            columnGroups,
            detailFields,
            measures,
            sorts,
            predicates,
            parameters,
            paging,
            default);

    private sealed class StubSource(IReadOnlyList<PostgresReportDatasetBinding> datasets)
        : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => datasets;
    }
}
