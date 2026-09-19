using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngineFullCoverageTests
{
    [Fact]
    public async Task ConstructorAndExport_RejectMissingExecutorAndNullRequest()
    {
        var definition = Definition();
        var provider = new DefinitionProvider(definition);
        var validator = new NoopValidator();

        Action nullExecutor = () => new ReportEngine(
            provider,
            validator,
            new ReportExecutionPlanner(),
            null!,
            new ReportSheetBuilder());
        Action nullDefinitions = () => new ReportEngine(
            null!, validator, new ReportExecutionPlanner(), new CapturingExecutor(), new ReportSheetBuilder());
        Action nullValidator = () => new ReportEngine(
            provider, null!, new ReportExecutionPlanner(), new CapturingExecutor(), new ReportSheetBuilder());
        Action nullPlanner = () => new ReportEngine(
            provider, validator, null!, new CapturingExecutor(), new ReportSheetBuilder());
        Action nullSheetBuilder = () => new ReportEngine(
            provider, validator, new ReportExecutionPlanner(), new CapturingExecutor(), null!);
        nullExecutor.Should().Throw<NgbConfigurationViolationException>();
        nullDefinitions.Should().Throw<NgbConfigurationViolationException>();
        nullValidator.Should().Throw<NgbConfigurationViolationException>();
        nullPlanner.Should().Throw<NgbConfigurationViolationException>();
        nullSheetBuilder.Should().Throw<NgbConfigurationViolationException>();

        var sut = new EngineFixture(definition).Sut;
        var action = () => sut.ExecuteExportSheetAsync(definition.ReportCode, null!, default);
        var nullExecution = () => sut.ExecuteAsync(definition.ReportCode, null!, default);
        (await action.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
        (await nullExecution.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task Execute_NormalizesExcessiveInteractiveOffsetAndLimit()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        fixture.Executor.Page = DataPage(Row("A", 10m));

        var result = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Offset: int.MaxValue, Limit: int.MaxValue),
            default);

        fixture.Executor.Paging!.Offset.Should().Be(PagingLimits.MaxOffset);
        fixture.Executor.Paging!.Limit.Should().Be(PagingLimits.MaxPageSize);
    }

    [Fact]
    public async Task Execute_DisablePagingUsesHardSourceCapAndRejectsTruncatedResult()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        fixture.Executor.Page = DataPage(Row("A", 10m)) with { HasMore = true };

        var action = () => fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(DisablePaging: true),
            default);

        await action.Should().ThrowAsync<NGB.Core.Reporting.Exceptions.ReportLayoutValidationException>()
            .WithMessage("*more than 10000 source rows*");
        fixture.Executor.Paging.Should().Be(new ReportPlanPaging(0, 10_001));
    }

    [Theory]
    [InlineData("detail", "layout.rowGroups")]
    [InlineData("column", "layout.columnGroups")]
    [InlineData("measure", "layout.measures")]
    public async Task Execute_BoundedInternalCaps_ReportTheRelevantLayoutPath(
        string shape,
        string expectedFieldPath)
    {
        var definition = Definition() with { DefaultLayout = LayoutForCap(shape) };

        var hardFixture = new EngineFixture(definition);
        hardFixture.Executor.Page = DataPage(Row("A", 10m)) with { HasMore = true };
        var hard = () => hardFixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(DisablePaging: true),
            default);
        var hardError = await hard.Should()
            .ThrowAsync<NGB.Core.Reporting.Exceptions.ReportLayoutValidationException>();
        hardError.Which.Context["fieldPath"].Should().Be(expectedFieldPath);
    }

    [Fact]
    public async Task Execute_DisablePagingRestoresExactTotalAfterBoundedMaterialization()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        fixture.Executor.Page = DataPage(Row("A", 10m), Row("B", 20m)) with { Total = null };

        var result = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(DisablePaging: true),
            default);

        fixture.Executor.Paging.Should().Be(new ReportPlanPaging(0, 10_001));
        result.Total.Should().Be(2);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Enrichment_CoversNoIdsMissingRefsStringFallbackRowAndColumnGroups()
    {
        var definition = Definition(groupedPagingMode: ReportGroupedPagingMode.BoundedNoCursor);
        var display = new DisplayReader();
        var fixture = new EngineFixture(definition, displayReader: display);
        fixture.Executor.Page = DataPage(Row("A", 1m));
        await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(DisablePaging: true),
            default);
        display.Calls.Should().Be(0);

        fixture.Executor.Page = new ReportDataPage(
            Columns: [],
            Rows:
            [
                new(new Dictionary<string, object?>
                {
                    ["document_display"] = "invalid",
                    [ReportInteractiveSupport.SupportDocumentId] = Guid.Empty,
                    ["document_id"] = "not-a-guid"
                }),
                new(new Dictionary<string, object?>
                {
                    ["document_display"] = "number",
                    [ReportInteractiveSupport.SupportDocumentId] = 42
                })
            ],
            Offset: 0,
            Limit: 10,
            Total: 2,
            HasMore: false);

        await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(DetailFields: ["document_display"], ShowDetails: true),
                Limit: 10),
            default);
        display.Calls.Should().Be(0);

        var unresolved = Guid.CreateVersion7();
        fixture.Executor.Page = new ReportDataPage(
            Columns: [],
            Rows: [DocumentRow("unresolved", unresolved, null, 1m)],
            Offset: 0,
            Limit: 10,
            Total: 1,
            HasMore: false);
        await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(DetailFields: ["document_display"], ShowDetails: true),
                Limit: 10),
            default);
        display.Calls.Should().Be(1);

        var resolvedFromSupport = Guid.CreateVersion7();
        var resolvedFromString = Guid.CreateVersion7();
        var missing = Guid.CreateVersion7();
        display.Items[resolvedFromSupport] = new(resolvedFromSupport, "doc.support", "Resolved support");
        display.Items[resolvedFromString] = new(resolvedFromString, "doc.string", "Resolved string");
        fixture.Executor.Page = new ReportDataPage(
            Columns: [],
            Rows:
            [
                DocumentRow("raw support", resolvedFromSupport, null, 10m),
                DocumentRow("raw string", null, resolvedFromString.ToString("D"), 20m),
                DocumentRow("missing", missing, resolvedFromString, 30m),
                DocumentRow("none", null, null, 40m)
            ],
            Offset: 0,
            Limit: 10,
            Total: 4,
            HasMore: false);

        var rowGrouped = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(RowGroups: [new ReportGroupingDto("document_display")]),
                DisablePaging: true),
            default);

        display.Calls.Should().Be(2);
        display.LastIds.Should().BeEquivalentTo([resolvedFromSupport, resolvedFromString, missing]);
        rowGrouped.Sheet.Rows.SelectMany(x => x.Cells).Select(x => x.Display).Should().Contain("Resolved support");

        fixture.Executor.Page = new ReportDataPage(
            Columns: [],
            Rows: [DocumentRow("raw column", resolvedFromSupport, null, 10m)],
            Offset: 0,
            Limit: 10,
            Total: 1,
            HasMore: false);
        var columnGrouped = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(
                    ColumnGroups: [new ReportGroupingDto("document_display")],
                    Measures: [new ReportMeasureSelectionDto("amount")],
                    ShowGrandTotals: true),
                DisablePaging: true),
            default);

        columnGrouped.Sheet.HeaderRows.Should().NotBeNull();
        display.Calls.Should().Be(3);
    }

    private static ReportDefinitionDto Definition(
        int? initialPageSize = 4,
        bool includePresentation = true,
        ReportGroupedPagingMode groupedPagingMode = ReportGroupedPagingMode.Standard)
        => new(
            "test.report_engine.full",
            "Engine report",
            Mode: ReportExecutionMode.Composable,
            Dataset: new ReportDatasetDto(
                "test.report_engine.dataset",
                Fields:
                [
                    new ReportFieldDto("period", "Period", "datetime", ReportFieldKind.Time),
                    new ReportFieldDto("group", "Group", "string", ReportFieldKind.Dimension),
                    new ReportFieldDto("document_display", "Document", "string", ReportFieldKind.Detail),
                    new ReportFieldDto("document_id", "Document", "uuid", ReportFieldKind.Dimension)
                ],
                Measures:
                [
                    new ReportMeasureDto("amount", "Amount", "decimal", [ReportAggregationKind.Sum, ReportAggregationKind.Min])
                ]),
            DefaultLayout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("group")],
                Measures: [new ReportMeasureSelectionDto("amount")],
                ShowSubtotals: false,
                ShowGrandTotals: true),
            Presentation: includePresentation
                ? new ReportPresentationDto(initialPageSize, GroupedPagingMode: groupedPagingMode)
                : null);

    private static ReportDefinitionRuntimeModel Runtime(ReportDefinitionDto definition) => new(definition);

    private static ReportQueryPlan Plan(
        bool rowGroups = false,
        bool columnGroups = false,
        bool showGrandTotals = false,
        bool showSubtotals = false)
        => new(
            "test.report_engine.full",
            "test.report_engine.dataset",
            ReportExecutionMode.Composable,
            rowGroups
                ? [new NGB.Runtime.Reporting.Planning.ReportPlanGrouping("group", "group", "Group", "string", IsColumnAxis: false)]
                : [],
            columnGroups
                ? [new NGB.Runtime.Reporting.Planning.ReportPlanGrouping("group", "group", "Group", "string", IsColumnAxis: true)]
                : [],
            [],
            [],
            [],
            [],
            [],
            new NGB.Runtime.Reporting.Planning.ReportPlanShape(
                false,
                showSubtotals,
                false,
                showGrandTotals,
                columnGroups),
            new NGB.Runtime.Reporting.Planning.ReportPlanPaging(0, 10, null));

    private static ReportLayoutDto LayoutForCap(string shape)
        => shape switch
        {
            "detail" => new ReportLayoutDto(
                DetailFields: ["document_display"],
                Measures: [new ReportMeasureSelectionDto("amount")],
                ShowDetails: true,
                ShowGrandTotals: true),
            "column" => new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("group")],
                Measures: [new ReportMeasureSelectionDto("amount")]),
            _ => new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("amount")],
                ShowGrandTotals: true)
        };

    private static ReportDataRow Row(string group, decimal amount)
        => new(new Dictionary<string, object?> { ["group"] = group, ["amount__sum"] = amount });

    private static ReportDataRow DocumentRow(string display, object? supportId, object? documentId, decimal amount)
        => new(new Dictionary<string, object?>
        {
            ["document_display"] = display,
            [ReportInteractiveSupport.SupportDocumentId] = supportId,
            ["document_id"] = documentId,
            ["amount__sum"] = amount
        });

    private static ReportDataPage DataPage(params ReportDataRow[] rows)
        => new(
            Columns: [],
            Rows: rows,
            Offset: 0,
            Limit: 100,
            Total: rows.Length,
            HasMore: false,
            Diagnostics: new Dictionary<string, string> { ["executor"] = "test" });

    private static ReportSheetRowDto SheetRow(string display, string semanticRole)
        => new(ReportRowKind.Detail, [new ReportCellDto(Display: display)], SemanticRole: semanticRole);

    private sealed class EngineFixture
    {
        public CapturingExecutor Executor { get; } = new();
        public ReportEngine Sut { get; }

        public EngineFixture(ReportDefinitionDto definition, IDocumentDisplayReader? displayReader = null)
        {
            Sut = new ReportEngine(
                new DefinitionProvider(definition),
                new NoopValidator(),
                new ReportExecutionPlanner(),
                Executor,
                new ReportSheetBuilder(),
                documentDisplayReader: displayReader);
        }
    }

    private sealed class DefinitionProvider(ReportDefinitionDto definition) : IReportDefinitionProvider
    {
        public Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportDefinitionDto>>([definition]);

        public Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct)
            => Task.FromResult(definition);
    }

    private sealed class NoopValidator : IReportLayoutValidator
    {
        public void Validate(ReportDefinitionDto definition, ReportExecutionRequestDto request)
        {
        }
    }

    private sealed class CapturingExecutor : IReportPlanExecutor
    {
        public ReportDataPage Page { get; set; } = DataPage(Row("A", 1m));
        public ReportPlanPaging? Paging { get; private set; }
        public int CallCount { get; private set; }

        public Task<ReportDataPage> ExecuteAsync(
            ReportDefinitionDto definition,
            ReportExecutionRequestDto request,
            string reportCode,
            string? datasetCode,
            IReadOnlyList<ReportPlanGrouping> rowGroups,
            IReadOnlyList<ReportPlanGrouping> columnGroups,
            IReadOnlyList<ReportPlanFieldSelection> detailFields,
            IReadOnlyList<ReportPlanMeasure> measures,
            IReadOnlyList<ReportPlanSort> sorts,
            IReadOnlyList<ReportPlanPredicate> predicates,
            IReadOnlyList<ReportPlanParameter> parameters,
            ReportPlanPaging paging,
            CancellationToken ct)
        {
            CallCount++;
            Paging = paging;
            return Task.FromResult(Page with
            {
                Offset = paging.Offset,
                Limit = paging.Limit
            });
        }
    }

    private sealed class DisplayReader : IDocumentDisplayReader
    {
        public Dictionary<Guid, DocumentDisplayRef> Items { get; } = [];
        public int Calls { get; private set; }
        public IReadOnlyCollection<Guid> LastIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                Items.ToDictionary(x => x.Key, x => x.Value.Display));

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct = default)
        {
            Calls++;
            LastIds = ids.ToArray();
            return Task.FromResult<IReadOnlyDictionary<Guid, DocumentDisplayRef>>(Items);
        }
    }
}
