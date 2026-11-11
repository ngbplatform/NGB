using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class AccountingLedgerAnalysisOrderedSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Period_leading_detail_pages_keep_the_existing_grouping_and_cursor_columns(bool continuation)
    {
        var request = Request();
        if (continuation)
            request = request with { Paging = new(0, 100, "existing-cursor") };

        var source = AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request);

        source.Should().NotBeNull();
        source!.FromSql.Should().Contain("ORDER BY period, entry_id").And.Contain("OFFSET 0");
        source.FromSql.Should().NotContain("LIMIT");
        source.BaseWhereSql.Should().BeNull("the exact UTC range is enforced inside the ordered source");
    }

    [Theory]
    [InlineData("export")]
    [InlineData("row-group")]
    [InlineData("column-group")]
    [InlineData("filter")]
    [InlineData("selection")]
    [InlineData("distinct")]
    [InlineData("missing-field")]
    [InlineData("extra-field")]
    [InlineData("duplicate-field")]
    [InlineData("no-measures")]
    [InlineData("average")]
    [InlineData("unknown-measure")]
    [InlineData("descending-period")]
    [InlineData("label-leading")]
    [InlineData("measure-sort")]
    [InlineData("grain-sort")]
    [InlineData("column-sort")]
    [InlineData("unknown-sort")]
    [InlineData("implicit-label-leading")]
    public void Other_shapes_retain_the_general_dataset_source(string shape)
    {
        var request = Request();
        request = shape switch
        {
            "export" => request with { Paging = new(0, 100, DisablePaging: true) },
            "row-group" => request with { RowGroups = [new("account_display", "account", "Account", "string")] },
            "column-group" => request with { ColumnGroups = [new("period_utc", "period", "Period", "datetime")] },
            "filter" => request with { Predicates = [new("account_id", "account", "Account", "uuid", new(System.Text.Json.JsonSerializer.SerializeToElement(Guid.NewGuid())))] },
            "selection" => request with { Selection = new([new("document_display")], []) },
            "distinct" => request with { DistinctGroups = true },
            "missing-field" => request with { DetailFields = request.DetailFields.Take(2).ToArray() },
            "extra-field" => request with { DetailFields = [.. request.DetailFields, new("entry_id", "entry", "Entry", "int64")] },
            "duplicate-field" => request with { DetailFields = [request.DetailFields[0], request.DetailFields[0], request.DetailFields[2]] },
            "no-measures" => request with { Measures = [] },
            "average" => request with { Measures = [request.Measures[0] with { Aggregation = ReportAggregationKind.Average }] },
            "unknown-measure" => request with { Measures = [request.Measures[0] with { MeasureCode = "custom" }] },
            "descending-period" => request with { Sorts = [new("period_utc", null, ReportSortDirection.Desc)] },
            "label-leading" => request with { Sorts = [new("account_display", null, ReportSortDirection.Asc)] },
            "measure-sort" => request with { Sorts = [request.Sorts[0], new("debit_amount", "debit", ReportSortDirection.Desc)] },
            "grain-sort" => request with { Sorts = [request.Sorts[0] with { TimeGrain = ReportTimeGrain.Month }] },
            "column-sort" => request with { Sorts = [request.Sorts[0] with { AppliesToColumnAxis = true }] },
            "unknown-sort" => request with { Sorts = [request.Sorts[0], new("custom", null, ReportSortDirection.Asc)] },
            _ => request with { Sorts = [], DetailFields = [request.DetailFields[1], request.DetailFields[0], request.DetailFields[2]] }
        };

        AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request).Should().BeNull();
    }

    [Fact]
    public void Implicit_period_order_and_descending_secondary_labels_are_supported()
    {
        var request = Request();
        AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request with { Sorts = [] })
            .Should().NotBeNull();
        AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request with
        {
            Sorts = [request.Sorts[0], new("account_display", null, ReportSortDirection.Desc)]
        }).Should().NotBeNull();
    }

    private static PostgresReportExecutionRequest Request() => new(
        "accounting.ledger.analysis", [], [],
        [new("period_utc", "period", "Period", "datetime"),
         new("account_display", "account", "Account", "string"),
         new("document_display", "document", "Document", "string")],
        [new("debit_amount", "debit", "Debit", "decimal", ReportAggregationKind.Sum)],
        [new("period_utc", null, ReportSortDirection.Asc), new("account_display", null, ReportSortDirection.Asc)],
        [], new Dictionary<string, object?>(), new(0, 100));
}
