using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class PostgresReportSqlBuilder_P0Tests
{
    [Fact]
    public void DatasetCatalog_Rejects_Duplicate_Postgres_Dataset_Bindings()
    {
        var act = () => new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis")),
            new StubDatasetSource(BuildDatasetBinding("ACCOUNTING.LEDGER.ANALYSIS"))
        ]);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*Duplicate PostgreSQL reporting dataset binding*");
    }

    [Fact]
    public void SqlBuilder_Builds_Parameterized_Grouped_Query()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups:
            [
                new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string"),
                new PostgresReportGroupingSelection("period_utc", "period_utc__month", "Period", "date", ReportTimeGrain.Month)
            ],
            ColumnGroups: [],
            DetailFields: [],
            Measures:
            [
                new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)
            ],
            Sorts:
            [
                new PostgresReportSortSelection("period_utc", null, ReportSortDirection.Desc, ReportTimeGrain.Month)
            ],
            Predicates:
            [
                new PostgresReportPredicateSelection("property_id", "property_id", "Property", "uuid", BuildScalarFilter("00000000-0000-0000-0000-000000000123"))
            ],
            Parameters: new Dictionary<string, object?>
            {
                ["from_utc"] = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ["to_utc_exclusive"] = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            Paging: new PostgresReportPaging(10, 25));

        var statement = sut.Build(request);

        statement.Sql.Should().Contain("FROM accounting_register_main r");
        statement.Sql.Should().Contain("r.account_code AS account_code");
        statement.Sql.Should().Contain("date_trunc('month', r.period_utc) AS period_utc__month");
        statement.Sql.Should().Contain("SUM(r.debit_amount) AS debit__sum");
        statement.Sql.Should().Contain("WHERE r.property_id = @p_0");
        statement.Sql.Should().Contain("GROUP BY");
        statement.Sql.Should().Contain("ORDER BY period_utc__month DESC");
        statement.Sql.Should().Contain("OFFSET @offset");
        statement.Sql.Should().Contain("LIMIT @limit_plus_one");
        statement.Parameters.ParameterNames.Should().Contain(["p_0", "offset", "limit_plus_one"]);
        statement.Columns.Should().HaveCount(3);
        statement.IsAggregated.Should().BeTrue();
    }

    [Fact]
    public void SqlBuilder_Omits_Paging_Clauses_For_Unpaged_Exports()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups:
            [
                new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")
            ],
            ColumnGroups: [],
            DetailFields: [],
            Measures:
            [
                new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)
            ],
            Sorts: [],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(10, 25, DisablePaging: true));

        var statement = sut.Build(request);

        statement.Sql.Should().NotContain("OFFSET @offset");
        statement.Sql.Should().NotContain("LIMIT @limit_plus_one");
        statement.Parameters.ParameterNames.Should().NotContain(["offset", "limit_plus_one"]);
        statement.Offset.Should().Be(0);
        statement.Limit.Should().Be(0);
    }

    [Fact]
    public void SqlBuilder_Builds_Normalized_Pivot_Fact_Query_With_ColumnGroups()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups:
            [
                new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")
            ],
            ColumnGroups:
            [
                new PostgresReportGroupingSelection("period_utc", "period_utc__month", "Period", "date", ReportTimeGrain.Month)
            ],
            DetailFields: [],
            Measures:
            [
                new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)
            ],
            Sorts: [],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20));

        var statement = sut.Build(request);

        statement.Sql.Should().Contain("r.account_code AS account_code");
        statement.Sql.Should().Contain("date_trunc('month', r.period_utc) AS period_utc__month");
        statement.Sql.Should().Contain("SUM(r.debit_amount) AS debit__sum");
        statement.Sql.Should().Contain("GROUP BY");
        statement.Sql.Should().Contain("ORDER BY account_code, period_utc__month");
        statement.Columns.Select(x => x.SemanticRole).Should().Equal("row-group", "column-group", "measure");
    }

    [Fact]
    public void SqlBuilder_Builds_Detail_Query_Without_GroupBy_When_No_Measures_Selected()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups: [],
            ColumnGroups: [],
            DetailFields:
            [
                new PostgresReportFieldSelection("document_display", "document_display", "Document", "string"),
                new PostgresReportFieldSelection("period_utc", "period_utc", "Period", "date")
            ],
            Measures: [],
            Sorts:
            [
                new PostgresReportSortSelection("period_utc", null, ReportSortDirection.Asc)
            ],
            Predicates:
            [
                new PostgresReportPredicateSelection("party_id", "party_id", "Party", "uuid", BuildArrayFilter(["a", "b"]))
            ],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 50));

        var statement = sut.Build(request);

        statement.Sql.Should().Contain("r.document_display AS document_display");
        statement.Sql.Should().Contain("r.period_utc AS period_utc");
        statement.Sql.Should().Contain("r.party_id = ANY(@p_0)");
        statement.Sql.Should().NotContain("GROUP BY");
        statement.IsAggregated.Should().BeFalse();
    }

    [Fact]
    public void SqlBuilder_Separates_Multiple_Predicates_With_And_Whitespace()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups:
            [
                new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")
            ],
            ColumnGroups: [],
            DetailFields: [],
            Measures:
            [
                new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)
            ],
            Sorts: [],
            Predicates:
            [
                new PostgresReportPredicateSelection("property_id", "property_id", "Property", "uuid", BuildScalarFilter("00000000-0000-0000-0000-000000000111")),
                new PostgresReportPredicateSelection("party_id", "party_id", "Party", "uuid", BuildScalarFilter("00000000-0000-0000-0000-000000000222"))
            ],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20));

        var statement = sut.Build(request);

        statement.Sql.Should().Contain("WHERE r.property_id = @p_0 AND r.party_id = @p_1");
    }

    [Fact]
    public void SqlBuilder_Uses_Quarter_Bucket_For_Period_Grouping()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups: [new PostgresReportGroupingSelection("period_utc", "period_utc__quarter", "Period", "date", ReportTimeGrain.Quarter)],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)],
            Sorts: [new PostgresReportSortSelection("period_utc", null, ReportSortDirection.Asc, ReportTimeGrain.Quarter)],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20));

        var statement = sut.Build(request);

        statement.Sql.Should().Contain("date_trunc('quarter', r.period_utc)");
    }

    [Fact]
    public void SqlBuilder_Rejects_Sort_By_Unselected_Field()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups: [new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)],
            Sorts: [new PostgresReportSortSelection("property_id", null, ReportSortDirection.Asc)],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20));

        var act = () => sut.Build(request);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*sort field 'property_id' is not selected*");
    }

    [Fact]
    public void SqlBuilder_Rejects_Duplicate_Projected_Aliases_Defense_In_Depth()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(BuildDatasetBinding("accounting.ledger.analysis"))
        ]));

        var request = new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups: [new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")],
            ColumnGroups: [new PostgresReportGroupingSelection("account_code", "account_code", "Account", "string")],
            DetailFields: [],
            Measures: [new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)],
            Sorts: [],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20));

        var act = () => sut.Build(request);

        act.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*duplicate projected alias 'account_code'*");
    }

    [Fact]
    public void AddPostgres_Registers_Reporting_Foundation_Services()
    {
        var services = new ServiceCollection();
        services.AddPostgres(o => o.ConnectionString = "Host=localhost;Database=test;Username=test;Password=test");

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresReportDatasetCatalog));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresReportSqlBuilder));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresReportDatasetExecutor));
    }

    private static PostgresReportDatasetBinding BuildDatasetBinding(string datasetCode)
        => new(
            datasetCode,
            fromSql: "accounting_register_main r",
            fields:
            [
                new PostgresReportFieldBinding("account_code", "r.account_code", "string"),
                new PostgresReportFieldBinding("property_id", "r.property_id", "uuid"),
                new PostgresReportFieldBinding("party_id", "r.party_id", "uuid"),
                new PostgresReportFieldBinding("document_display", "r.document_display", "string"),
                new PostgresReportFieldBinding(
                    "period_utc",
                    "r.period_utc",
                    "date",
                    dayBucketSqlExpression: "date_trunc('day', r.period_utc)",
                    weekBucketSqlExpression: "date_trunc('week', r.period_utc)",
                    monthBucketSqlExpression: "date_trunc('month', r.period_utc)",
                    quarterBucketSqlExpression: "date_trunc('quarter', r.period_utc)",
                    yearBucketSqlExpression: "date_trunc('year', r.period_utc)")
            ],
            measures:
            [
                new PostgresReportMeasureBinding("debit", "r.debit_amount", "decimal"),
                new PostgresReportMeasureBinding("credit", "r.credit_amount", "decimal")
            ]);

    private static ReportFilterValueDto BuildScalarFilter(string value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new ReportFilterValueDto(doc.RootElement.Clone());
    }

    private static ReportFilterValueDto BuildArrayFilter(IReadOnlyList<string> values)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return new ReportFilterValueDto(doc.RootElement.Clone());
    }

    private sealed class StubDatasetSource(PostgresReportDatasetBinding dataset) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [dataset];
    }

    [Fact]
    public void SqlBuilder_Adds_Hidden_Support_Fields_For_Clickable_Account_And_Document_Display_On_Both_Axes()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(new PostgresReportDatasetBinding(
                "accounting.ledger.analysis",
                fromSql: "fact f",
                fields:
                [
                    new PostgresReportFieldBinding("account_display", "f.account_display", "string"),
                    new PostgresReportFieldBinding("account_id", "f.account_id", "uuid"),
                    new PostgresReportFieldBinding("document_display", "f.document_display", "string"),
                    new PostgresReportFieldBinding("document_id", "f.document_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("debit", "f.debit_amount", "decimal")
                ]))
        ]));

        var statement = sut.Build(new PostgresReportExecutionRequest(
            DatasetCode: "accounting.ledger.analysis",
            RowGroups:
            [
                new PostgresReportGroupingSelection("account_display", "account_display", "Account", "string")
            ],
            ColumnGroups:
            [
                new PostgresReportGroupingSelection("document_display", "document_display", "Document", "string")
            ],
            DetailFields: [],
            Measures: [new PostgresReportMeasureSelection("debit", "debit__sum", "Debit", "decimal", ReportAggregationKind.Sum)],
            Sorts: [],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20)));

        statement.Sql.Should().Contain($"f.account_id AS {ReportInteractiveSupport.SupportAccountId}");
        statement.Sql.Should().Contain($"f.document_id AS {ReportInteractiveSupport.SupportDocumentId}");
        statement.Columns.Select(x => x.OutputCode).Should().Contain([ReportInteractiveSupport.SupportAccountId, ReportInteractiveSupport.SupportDocumentId]);
    }

    [Fact]
    public void SqlBuilder_Adds_Hidden_Paired_Id_Fields_For_Clickable_Catalog_Display_Columns()
    {
        var sut = new PostgresReportSqlBuilder(new PostgresReportDatasetCatalog([
            new StubDatasetSource(new PostgresReportDatasetBinding(
                "trade.inventory",
                fromSql: "fact f",
                fields:
                [
                    new PostgresReportFieldBinding("warehouse_display", "f.warehouse_display", "string"),
                    new PostgresReportFieldBinding("warehouse_id", "f.warehouse_id", "uuid"),
                    new PostgresReportFieldBinding("item_display", "f.item_display", "string"),
                    new PostgresReportFieldBinding("item_id", "f.item_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("quantity_on_hand", "f.quantity_on_hand", "decimal")
                ]))
        ]));

        var statement = sut.Build(new PostgresReportExecutionRequest(
            DatasetCode: "trade.inventory",
            RowGroups:
            [
                new PostgresReportGroupingSelection("warehouse_display", "warehouse_display", "Warehouse", "string")
            ],
            ColumnGroups: [],
            DetailFields:
            [
                new PostgresReportFieldSelection("item_display", "item_display", "Item", "string")
            ],
            Measures:
            [
                new PostgresReportMeasureSelection("quantity_on_hand", "quantity_on_hand__sum", "Quantity On Hand", "decimal", ReportAggregationKind.Sum)
            ],
            Sorts: [],
            Predicates: [],
            Parameters: new Dictionary<string, object?>(),
            Paging: new PostgresReportPaging(0, 20)));

        statement.Sql.Should().Contain("f.warehouse_id AS warehouse_id");
        statement.Sql.Should().Contain("f.item_id AS item_id");
        statement.Columns.Select(x => x.OutputCode).Should().Contain(["warehouse_id", "item_id"]);
    }
}
