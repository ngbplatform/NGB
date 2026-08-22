using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Planning;
using NGB.Runtime.Reporting.Rendering;
using NGB.Tools.Exceptions;
using Xunit;
using ReportDataRow = NGB.Application.Abstractions.Services.ReportDataRow;
using ReportInteractiveSupport = NGB.Application.Abstractions.Services.ReportInteractiveSupport;

namespace NGB.Runtime.Tests.Reporting.Rendering;

public sealed class ReportRenderingHelperFullCoverageTests
{
    [Fact]
    public void GroupTreeBuilder_WhenDependenciesOrArgumentsAreNull_ThrowsRequiredArgument()
    {
        var formatter = new ReportCellFormatter();
        var subtotal = new ReportSubtotalBuilder(formatter);
        var resolver = new ReportComposableCellActionResolver(Plan());

        Action missingFormatter = () => new ReportGroupTreeBuilder(null!, subtotal, resolver);
        Action missingSubtotal = () => new ReportGroupTreeBuilder(formatter, null!, resolver);
        Action missingResolver = () => new ReportGroupTreeBuilder(formatter, subtotal, null!);
        missingFormatter.Should().Throw<NgbArgumentRequiredException>();
        missingSubtotal.Should().Throw<NgbArgumentRequiredException>();
        missingResolver.Should().Throw<NgbArgumentRequiredException>();

        var builder = GroupTreeBuilder();
        Action missingPlan = () => builder.BuildRows(null!, [], []);
        Action missingColumns = () => builder.BuildRows(Plan(), null!, []);
        Action missingRows = () => builder.BuildRows(Plan(), [], null!);
        missingPlan.Should().Throw<NgbArgumentRequiredException>();
        missingColumns.Should().Throw<NgbArgumentRequiredException>();
        missingRows.Should().Throw<NgbArgumentRequiredException>();
        builder.BuildRows(Plan(), [], []).Should().BeEmpty();
    }

    [Fact]
    public void GroupTreeBuilder_FlatRows_CoversNoMeasuresPureTotalAndRegularGrandTotal()
    {
        var builder = GroupTreeBuilder();
        var detail = Detail("name");
        var measure = Measure("amount", "Amount");
        var columns = new[]
        {
            new ReportSheetColumnDto("name", "Name", "string"),
            new ReportSheetColumnDto("amount", "Amount", "decimal", SemanticRole: "measure")
        };

        var withoutMeasures = builder.BuildRows(
            Plan(detailFields: [detail]),
            columns,
            [Row(("name", "A")), Row(("name", "B"))]);
        withoutMeasures.Select(row => row.RowKind).Should().Equal(ReportRowKind.Detail, ReportRowKind.Detail);

        var pureTotal = builder.BuildRows(
            Plan(
                measures: [measure],
                shape: new ReportPlanShape(false, false, false, true, false)),
            columns,
            [Row(("amount", 10m))]);
        pureTotal.Should().ContainSingle().Which.RowKind.Should().Be(ReportRowKind.Total);
        pureTotal[0].Cells.Select(cell => cell.Display).Should().Equal("Total", "10");

        var pureTotalHidden = builder.BuildRows(
            Plan(
                measures: [measure],
                shape: new ReportPlanShape(false, false, false, false, false)),
            columns,
            [Row(("amount", 10m))]);
        pureTotalHidden.Should().ContainSingle().Which.RowKind.Should().Be(ReportRowKind.Detail);

        var regular = builder.BuildRows(
            Plan(
                detailFields: [detail],
                measures: [measure],
                shape: new ReportPlanShape(true, false, false, true, false)),
            columns,
            [Row(("name", "A"), ("amount", 4m)), Row(("name", "B"), ("amount", 6m))]);
        regular.Select(row => row.RowKind).Should().Equal(
            ReportRowKind.Detail,
            ReportRowKind.Detail,
            ReportRowKind.Total);
        regular[^1].Cells[1].Display.Should().Be("10");

        var regularWithoutTotal = builder.BuildRows(
            Plan(
                detailFields: [detail],
                measures: [measure],
                shape: new ReportPlanShape(true, false, false, false, false)),
            columns,
            [Row(("name", "A"), ("amount", 4m))]);
        regularWithoutTotal.Should().ContainSingle().Which.RowKind.Should().Be(ReportRowKind.Detail);
    }

    [Fact]
    public void GroupTreeBuilder_GroupedRows_CoverInlineTotalsWithoutDetailsAndWithoutMeasures()
    {
        var builder = GroupTreeBuilder();
        var region = Group("region", "region", "Region");
        var city = Group("city", "city", "City");
        var measure = Measure("amount", "Amount");
        var columns = new[]
        {
            ReportRowHierarchy.CreateColumn([region]),
            new ReportSheetColumnDto("amount", "Amount", "decimal", SemanticRole: "measure")
        };

        var inlineWithoutDetails = builder.BuildRows(
            Plan(
                rowGroups: [region],
                measures: [measure],
                shape: new ReportPlanShape(false, false, true, false, false)),
            columns,
            [Row(("region", "East"), ("amount", 4m)), Row(("region", "East"), ("amount", 6m))]);
        inlineWithoutDetails.Should().ContainSingle().Which.RowKind.Should().Be(ReportRowKind.Group);
        inlineWithoutDetails[0].Cells[1].Display.Should().Be("10");

        var noMeasures = builder.BuildRows(
            Plan(
                rowGroups: [region, city],
                shape: new ReportPlanShape(false, true, true, true, false)),
            columns,
            [Row(("region", "East"), ("city", "Boston"))]);
        noMeasures.Should().OnlyContain(row => row.RowKind == ReportRowKind.Group);

        var inlineAllLevels = builder.BuildRows(
            Plan(
                rowGroups: [region, city],
                measures: [measure],
                shape: new ReportPlanShape(false, true, false, false, false)),
            columns,
            [Row(("region", "East"), ("city", "Boston"), ("amount", 5m))]);
        inlineAllLevels.Should().HaveCount(2);
        inlineAllLevels.Should().OnlyContain(row => row.RowKind == ReportRowKind.Group);
        inlineAllLevels.Should().OnlyContain(row => row.Cells[1].Display == "5");

        var explicitDetailsWithoutSubtotals = builder.BuildRows(
            Plan(
                rowGroups: [region],
                measures: [measure],
                shape: new ReportPlanShape(true, false, true, false, false)),
            columns,
            [Row(("region", "East"), ("amount", 5m))]);
        explicitDetailsWithoutSubtotals.Select(row => row.RowKind)
            .Should().Equal(ReportRowKind.Group, ReportRowKind.Detail);
        explicitDetailsWithoutSubtotals[0].Cells[1].Display.Should().BeNull();
    }

    [Fact]
    public void GroupTreeBuilder_GroupedDetails_EmitParentSubtotalsLeafInlineTotalsAndGrandTotal()
    {
        var builder = GroupTreeBuilder();
        var region = Group("region", "region", "Region");
        var city = Group("city", "city", "City");
        var name = Detail("name");
        var amount = Measure("amount", "Amount");
        var columns = new[]
        {
            ReportRowHierarchy.CreateColumn([region, city]),
            new ReportSheetColumnDto("name", "Name", "string"),
            new ReportSheetColumnDto("amount", "Amount", "decimal", SemanticRole: "measure"),
            new ReportSheetColumnDto("unused", "Unused", "string")
        };
        var plan = Plan(
            rowGroups: [region, city],
            detailFields: [name],
            measures: [amount],
            shape: new ReportPlanShape(false, true, true, true, false));

        var rows = builder.BuildRows(
            plan,
            columns,
            [
                Row(("region", "East"), ("city", "Boston"), ("name", "A"), ("amount", 4m)),
                Row(("region", "East"), ("city", "New York"), ("name", "B"), ("amount", 6m)),
                Row(("region", "West"), ("city", "Los Angeles"), ("name", "C"), ("amount", 5m))
            ]);

        rows.Should().Contain(row => row.RowKind == ReportRowKind.Detail && row.Cells[1].Display == "A" && row.Cells[2].Display == "4" && row.Cells[3].Display == null);
        rows.Should().Contain(row => row.RowKind == ReportRowKind.Subtotal && row.Cells[0].Display == "East subtotal" && row.Cells[2].Display == "10");
        rows.Should().Contain(row => row.RowKind == ReportRowKind.Group && row.Cells[0].Display == "Boston" && row.Cells[2].Display == "4");
        rows[^1].RowKind.Should().Be(ReportRowKind.Total);
        rows[^1].Cells[2].Display.Should().Be("15");
    }

    [Fact]
    public void SubtotalBuilder_WhenDependencyOrArgumentsAreNull_ThrowsRequiredArgument()
    {
        Action missingFormatter = () => new ReportSubtotalBuilder(null!);
        missingFormatter.Should().Throw<NgbArgumentRequiredException>();

        var builder = new ReportSubtotalBuilder(new ReportCellFormatter());
        Action missingMeasures = () => builder.CreateAccumulator(null!);
        Action missingAccumulator = () => builder.Add(null!, new Dictionary<string, object?>());
        var accumulator = builder.CreateAccumulator([]);
        Action missingValues = () => builder.Add(accumulator, null!);
        Action directMissingValues = () => accumulator.Add(null!);

        missingMeasures.Should().Throw<NgbArgumentRequiredException>();
        missingAccumulator.Should().Throw<NgbArgumentRequiredException>();
        missingValues.Should().Throw<NgbArgumentRequiredException>();
        directMissingValues.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void SubtotalAccumulator_AddsEverySupportedNumericRepresentationAndSkipsNulls()
    {
        var measures = new[]
        {
            new ReportPlanMeasure("count", "count", "Count", "int64", ReportAggregationKind.Sum),
            new ReportPlanMeasure("amount", "amount", "Amount", "decimal", ReportAggregationKind.Sum)
        };
        var accumulator = new ReportSubtotalAccumulator(measures);
        accumulator.Add(new Dictionary<string, object?> { ["count"] = null, ["amount"] = null });

        var values = new (object Count, object Amount)[]
        {
            (1L, 1m),
            (2, 2L),
            ((short)3, 3),
            ((byte)4, (short)4),
            (5m, (byte)5),
            (6d, 6d),
            (7f, 7f),
            ("8", "8")
        };
        foreach (var (count, amount) in values)
        {
            accumulator.Add(new Dictionary<string, object?>
            {
                ["count"] = count,
                ["amount"] = amount
            });
        }

        accumulator.TryGetValue("COUNT", out var countTotal).Should().BeTrue();
        countTotal.Should().Be(36L);
        accumulator.TryGetValue("amount", out var amountTotal).Should().BeTrue();
        amountTotal.Should().Be(36m);
        accumulator.TryGetValue("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void SubtotalBuilder_BuildSummaryRow_SelectsLabelMeasureAndBlankCells()
    {
        var builder = new ReportSubtotalBuilder(new ReportCellFormatter());
        var measures = new[]
        {
            new ReportPlanMeasure("amount", "amount", "Amount", "decimal", ReportAggregationKind.Sum)
        };
        var accumulator = builder.CreateAccumulator(measures);
        builder.Add(accumulator, new Dictionary<string, object?> { ["amount"] = 12.5m });
        var columns = new[]
        {
            new ReportSheetColumnDto("label", "Label", "string"),
            new ReportSheetColumnDto("amount", "Amount", "decimal", SemanticRole: "measure"),
            new ReportSheetColumnDto("unknown", "Unknown", "decimal", SemanticRole: "measure")
        };

        var row = builder.BuildSummaryRow(
            columns,
            accumulator,
            "Total",
            ReportRowKind.Total,
            2,
            "group-key",
            "grand-total");

        row.Cells.Select(cell => cell.Display).Should().Equal("Total", "12.5", null);
        row.Cells.Should().OnlyContain(cell => cell.SemanticRole == "grand-total");
        row.OutlineLevel.Should().Be(2);
        row.GroupKey.Should().Be("group-key");

        var allMeasures = builder.BuildSummaryRow(
            [new ReportSheetColumnDto("amount", "Amount", "decimal", SemanticRole: "measure")],
            accumulator,
            "Fallback label",
            ReportRowKind.Subtotal,
            0,
            null,
            "subtotal");
        allMeasures.Cells.Should().ContainSingle().Which.Display.Should().Be("Fallback label");

        builder.BuildSummaryRow([], accumulator, "Empty", ReportRowKind.Total, 0, null, "total")
            .Cells.Should().BeEmpty();
    }

    [Fact]
    public void PivotHeaderBuilder_WhenDependenciesAreNull_ThrowsRequiredArgument()
    {
        var resolver = new ReportComposableCellActionResolver(Plan());

        Action missingFormatter = () => new ReportPivotHeaderBuilder(null!, resolver);
        Action missingResolver = () => new ReportPivotHeaderBuilder(new ReportCellFormatter(), null!);

        missingFormatter.Should().Throw<NgbArgumentRequiredException>();
        missingResolver.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void PivotHeaderBuilder_WhenInputCollectionIsNull_ThrowsRequiredArgument()
    {
        var builder = PivotHeaderBuilder();

        Action missingRowAxis = () => builder.Build(null!, [], [], [], false);
        Action missingColumnGroups = () => builder.Build([], null!, [], [], false);
        Action missingLeaves = () => builder.Build([], [], null!, [], false);
        Action missingMeasures = () => builder.Build([], [], [], null!, false);

        missingRowAxis.Should().Throw<NgbArgumentRequiredException>();
        missingColumnGroups.Should().Throw<NgbArgumentRequiredException>();
        missingLeaves.Should().Throw<NgbArgumentRequiredException>();
        missingMeasures.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void PivotHeaderBuilder_WhenThereAreNoColumnGroups_ReturnsEmptyRows()
    {
        PivotHeaderBuilder().Build([], [], [], [], includeTotals: false).Should().BeEmpty();
    }

    [Fact]
    public void PivotHeaderBuilder_BuildsMergedPrefixesMeasureRowsAndTotals()
    {
        var firstGroup = Group("region", "region", "Region");
        var secondGroup = Group("month", "month", "Month");
        var leaves = new[]
        {
            Leaf("a-x", ["A", "X"]),
            Leaf("a-y", ["A", "Y"]),
            Leaf("b-z", ["B", "Z"])
        };
        var measures = new[]
        {
            Measure("debit", "Debit"),
            Measure("credit", "Credit")
        };

        var rows = PivotHeaderBuilder().Build(
            [new ReportSheetColumnDto("rows", "Rows", "string")],
            [firstGroup, secondGroup],
            leaves,
            measures,
            includeTotals: true);

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.RowKind == ReportRowKind.Header && row.SemanticRole == "header");
        rows[0].Cells.Select(cell => cell.Display).Should().Equal("Rows", "A", "B", "Total");
        rows[0].Cells[0].RowSpan.Should().Be(3);
        rows[0].Cells[1].ColSpan.Should().Be(4);
        rows[0].Cells[2].ColSpan.Should().Be(2);
        rows[0].Cells[3].RowSpan.Should().Be(2);
        rows[1].Cells.Select(cell => cell.Display).Should().Equal("X", "Y", "Z");
        rows[2].Cells.Select(cell => cell.Display).Should().Equal(
            "Debit", "Credit", "Debit", "Credit", "Debit", "Credit", "Debit", "Credit");
    }

    [Fact]
    public void PivotHeaderBuilder_WithNoMeasures_UsesSingleColumnSpanAndOmitsTotals()
    {
        var rows = PivotHeaderBuilder().Build(
            [],
            [Group("region", "region", "Region")],
            [Leaf("a", ["A"])],
            [],
            includeTotals: false);

        rows.Should().HaveCount(2);
        rows[0].Cells.Should().ContainSingle().Which.ColSpan.Should().Be(1);
        rows[1].Cells.Should().BeEmpty();
    }

    [Fact]
    public void ReportRowHierarchy_CoversEmptyAndPopulatedShapes()
    {
        var formatter = new ReportCellFormatter();
        var month = Group("period", "period__month", "Period", ReportTimeGrain.Month);
        var account = Group("account_display", "account_display", "Account");
        var plan = Plan(
            rowGroups: [month, account],
            detailFields: [new ReportPlanFieldSelection("document_display", "document", "Document", "string")]);

        ReportRowHierarchy.HasHierarchy([]).Should().BeFalse();
        ReportRowHierarchy.HasHierarchy(plan.RowGroups).Should().BeTrue();
        ReportRowHierarchy.CreateColumn([]).Title.Should().Be("Rows");
        var column = ReportRowHierarchy.CreateColumn(plan.RowGroups);
        column.Code.Should().Be(ReportRowHierarchy.ColumnCode);
        column.Title.Should().Be("Period\nAccount");
        ReportRowHierarchy.IsHierarchyColumn(column).Should().BeTrue();
        ReportRowHierarchy.IsHierarchyColumn(new ReportSheetColumnDto("other", "Other", "string")).Should().BeFalse();
        ReportRowHierarchy.BuildValueCodes(plan).Should().Equal("period__month", "account_display", "document");
        ReportRowHierarchy.FormatGroupLabel(formatter, month, new DateOnly(2026, 3, 1)).Should().Be("March 2026");
        ReportRowHierarchy.FormatLeafLabel(formatter, [], []).Should().BeEmpty();
        ReportRowHierarchy.FormatLeafLabel(formatter, [month], []).Should().BeEmpty();
        ReportRowHierarchy.FormatLeafLabel(formatter, [month, account], [new DateOnly(2026, 3, 1)])
            .Should().Be("March 2026");
        ReportRowHierarchy.FormatLeafLabel(formatter, [month], [new DateOnly(2026, 3, 1), "ignored"])
            .Should().Be("March 2026");
    }

    [Fact]
    public void CellActionResolver_AccountActions_CoverSupportFallbackAndMissingDates()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accountGroup = Group("account_display", "account", "Account");
        var inherited = new ReportFilterValueDto(JsonSerializer.SerializeToElement("active"));
        var resolver = new ReportComposableCellActionResolver(
            Plan(
                rowGroups: [accountGroup],
                predicates: [new ReportPlanPredicate("status", "status", "Status", "string", inherited)],
                parameters:
                [
                    new ReportPlanParameter("FROM_UTC", "2026-03-01"),
                    new ReportPlanParameter("to_utc", "2026-03-31")
                ]));

        resolver.ResolveForDetailColumn("unknown", new Dictionary<string, object?>()).Should().BeNull();
        resolver.ResolveForDetailColumn("account", new Dictionary<string, object?>()).Should().BeNull();
        resolver.ResolveForGroup(accountGroup, new Dictionary<string, object?>
        {
            [ReportInteractiveSupport.SupportAccountId] = 123,
            ["account_id"] = accountId.ToString()
        }).Should().Match<ReportCellActionDto>(action =>
            action.Report!.Filters!["account_id"].Value.GetGuid() == accountId
            && action.Report.Filters["status"].Value.GetString() == "active");

        var supportAction = resolver.ResolveForGroup(accountGroup, new Dictionary<string, object?>
        {
            [ReportInteractiveSupport.SupportAccountId] = accountId
        });
        supportAction.Should().NotBeNull();
        supportAction!.Kind.Should().Be(ReportCellActionKinds.OpenReport);

        var withoutFrom = new ReportComposableCellActionResolver(
            Plan(parameters: [new ReportPlanParameter("to_utc", "2026-03-31")]));
        withoutFrom.ResolveForGroup(accountGroup, new Dictionary<string, object?>
        {
            ["account_id"] = accountId
        }).Should().BeNull();

        var withoutTo = new ReportComposableCellActionResolver(
            Plan(parameters:
            [
                new ReportPlanParameter("from_utc", "2026-03-01"),
                new ReportPlanParameter("to_utc", "not-a-date")
            ]));
        withoutTo.ResolveForGroup(accountGroup, new Dictionary<string, object?>
        {
            ["account_id"] = accountId
        }).Should().BeNull();
    }

    [Fact]
    public void CellActionResolver_DocumentActions_CoverSupportFallbackAndBlankType()
    {
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var documentGroup = Group("document_display", "document", "Document");
        var resolver = new ReportComposableCellActionResolver(Plan(rowGroups: [documentGroup]));

        resolver.ResolveForGroup(documentGroup, new Dictionary<string, object?>
        {
            [ReportInteractiveSupport.SupportDocumentId] = null,
            ["document_id"] = documentId.ToString(),
            [ReportInteractiveSupport.SupportDocumentType] = "sales.invoice"
        }).Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenDocument,
            DocumentType: "sales.invoice",
            DocumentId: documentId));

        resolver.ResolveForGroup(documentGroup, new Dictionary<string, object?>
        {
            [ReportInteractiveSupport.SupportDocumentId] = documentId,
            [ReportInteractiveSupport.SupportDocumentType] = " "
        }).Should().BeNull();

        resolver.ResolveForGroup(documentGroup, new Dictionary<string, object?>
        {
            [ReportInteractiveSupport.SupportDocumentId] = new object()
        }).Should().BeNull();
    }

    [Fact]
    public void CellActionResolver_DatasetLookups_CoverDocumentCatalogAndInvalidMetadata()
    {
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var catalogId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var dataset = new ReportDatasetDefinition(
            new ReportDatasetDto(
                "lookup.dataset",
                Fields:
                [
                    Field("source_document_id", new DocumentLookupSourceDto(["sales.invoice"])),
                    Field("source_document_display"),
                    Field("warehouse_id", new CatalogLookupSourceDto("inventory.warehouse")),
                    Field("warehouse_display"),
                    Field("multi_document_id", new DocumentLookupSourceDto(["sales.invoice", "sales.order"])),
                    Field("multi_document_display"),
                    Field("plain_id"),
                    Field("plain_display")
                ]));
        var plan = Plan(detailFields:
        [
            Detail("source_document_display"),
            Detail("warehouse_display"),
            Detail("multi_document_display"),
            Detail("plain_display"),
            Detail("plain")
        ]);
        var resolver = new ReportComposableCellActionResolver(plan, dataset);

        resolver.ResolveForDetailColumn("source_document_display", new Dictionary<string, object?>
        {
            ["source_document_id"] = documentId
        }).Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenDocument,
            DocumentType: "sales.invoice",
            DocumentId: documentId));

        resolver.ResolveForDetailColumn("warehouse_display", new Dictionary<string, object?>
        {
            ["warehouse_id"] = catalogId.ToString()
        }).Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: "inventory.warehouse",
            CatalogId: catalogId));

        resolver.ResolveForDetailColumn("multi_document_display", new Dictionary<string, object?>
        {
            ["multi_document_id"] = documentId
        }).Should().BeNull();
        resolver.ResolveForDetailColumn("plain_display", new Dictionary<string, object?>
        {
            ["plain_id"] = documentId
        }).Should().BeNull();
        resolver.ResolveForDetailColumn("plain", new Dictionary<string, object?>()).Should().BeNull();
        resolver.ResolveForDetailColumn("source_document_display", new Dictionary<string, object?>
        {
            ["source_document_id"] = 123
        }).Should().BeNull();
        resolver.ResolveForDetailColumn("missing_display", new Dictionary<string, object?>()).Should().BeNull();

        var withoutDataset = new ReportComposableCellActionResolver(plan);
        withoutDataset.ResolveForDetailColumn("warehouse_display", new Dictionary<string, object?>
        {
            ["warehouse_id"] = catalogId
        }).Should().BeNull();
    }

    private static ReportFieldDto Field(string code, LookupSourceDto? lookup = null)
        => new(code, code, "string", ReportFieldKind.Dimension, Lookup: lookup);

    private static ReportPlanFieldSelection Detail(string code)
        => new(code, code, code, "string");

    private static ReportPlanGrouping Group(
        string fieldCode,
        string outputCode,
        string label,
        ReportTimeGrain? timeGrain = null)
        => new(fieldCode, outputCode, label, "string", IsColumnAxis: false, TimeGrain: timeGrain);

    private static PivotColumnLeaf Leaf(string key, IReadOnlyList<object?> values)
        => new(
            key,
            values,
            values.Select(value => value?.ToString() ?? string.Empty).ToArray(),
            new Dictionary<string, object?>());

    private static ReportPlanMeasure Measure(string code, string label)
        => new(code, code, label, "decimal", ReportAggregationKind.Sum);

    private static ReportDataRow Row(params (string Code, object? Value)[] values)
        => new(new Dictionary<string, object?>(
            values.Select(value => new KeyValuePair<string, object?>(value.Code, value.Value)),
            StringComparer.OrdinalIgnoreCase));

    private static ReportGroupTreeBuilder GroupTreeBuilder()
    {
        var formatter = new ReportCellFormatter();
        return new ReportGroupTreeBuilder(
            formatter,
            new ReportSubtotalBuilder(formatter),
            new ReportComposableCellActionResolver(Plan()));
    }

    private static ReportPivotHeaderBuilder PivotHeaderBuilder()
        => new(
            new ReportCellFormatter(),
            new ReportComposableCellActionResolver(Plan()));

    private static ReportQueryPlan Plan(
        IReadOnlyList<ReportPlanGrouping>? rowGroups = null,
        IReadOnlyList<ReportPlanFieldSelection>? detailFields = null,
        IReadOnlyList<ReportPlanMeasure>? measures = null,
        IReadOnlyList<ReportPlanPredicate>? predicates = null,
        IReadOnlyList<ReportPlanParameter>? parameters = null,
        ReportPlanShape? shape = null)
        => new(
            ReportCode: "test.report",
            DatasetCode: null,
            Mode: ReportExecutionMode.Composable,
            RowGroups: rowGroups ?? [],
            ColumnGroups: [],
            Measures: measures ?? [],
            DetailFields: detailFields ?? [],
            Sorts: [],
            Predicates: predicates ?? [],
            Parameters: parameters ?? [],
            Shape: shape ?? new ReportPlanShape(false, false, false, false, false),
            Paging: new ReportPlanPaging(0, 20, null));
}
