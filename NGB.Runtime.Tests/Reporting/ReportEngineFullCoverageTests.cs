using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngineFullCoverageTests
{
    [Fact]
    public void RenderedSheetPagingPolicy_CoversEveryShortCircuitBranch()
    {
        var standard = Runtime(Definition());
        var withoutPresentation = Runtime(Definition(includePresentation: false));
        var bounded = Runtime(Definition(groupedPagingMode: ReportGroupedPagingMode.BoundedNoCursor));
        var canonical = Runtime(Definition() with { Mode = ReportExecutionMode.Canonical });
        var request = new ReportExecutionRequestDto();

        ReportEngine.ShouldUseRenderedSheetPaging(canonical, request, Plan()).Should().BeFalse();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request with { DisablePaging = true }, Plan(rowGroups: true)).Should().BeFalse();
        ReportEngine.ShouldUseRenderedSheetPaging(bounded, request, Plan(rowGroups: true)).Should().BeFalse();
        ReportEngine.ShouldUseRenderedSheetPaging(withoutPresentation, request, Plan(rowGroups: true)).Should().BeTrue();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request, Plan(rowGroups: true)).Should().BeTrue();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request, Plan(columnGroups: true)).Should().BeTrue();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request, Plan(showGrandTotals: true)).Should().BeTrue();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request, Plan(showSubtotals: true)).Should().BeTrue();
        ReportEngine.ShouldUseRenderedSheetPaging(standard, request, Plan()).Should().BeFalse();
    }

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

    [Theory]
    [InlineData(null, false, 0, 100)]
    [InlineData(null, true, 0, 100)]
    [InlineData(0, true, 0, 100)]
    [InlineData(7, true, 0, 7)]
    [InlineData(7, true, 5, 5)]
    public async Task Execute_ResolvesPositiveRequestedLimitOrPresentationFallback(
        int? initialPageSize,
        bool includePresentation,
        int requestLimit,
        int expectedLimit)
    {
        var definition = Definition(initialPageSize, includePresentation);
        var fixture = new EngineFixture(definition);
        fixture.Executor.Page = DataPage(Row("A", 10m));

        var result = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: requestLimit),
            default);

        fixture.Executor.Paging!.Limit.Should().Be(expectedLimit);
        result.Limit.Should().Be(expectedLimit);
        result.HasMore.Should().BeFalse();
        fixture.Store.RemoveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WhenSnapshotStoreUnavailable_UsesOffsetCursorAndRematerializesNextPage()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        fixture.Store.SetResult = false;
        fixture.Executor.Page = DataPage(Row("A", 10m), Row("B", 20m), Row("C", 30m)) with
        {
            Diagnostics = null
        };

        var first = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: 1, Offset: -5),
            default);

        first.Offset.Should().Be(0);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be(RenderedSheetCursorCodec.EncodeOffsetOnly(1));
        first.Diagnostics!["snapshotCache"].Should().Be("unavailable");

        var second = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: 1, Cursor: first.NextCursor),
            default);

        second.Offset.Should().Be(1);
        second.HasMore.Should().BeTrue();
        fixture.Executor.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_RejectsCursorFingerprintMismatch_ForComprehensiveGroupedPlan()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        var layout = new ReportLayoutDto(
            RowGroups:
            [
                new ReportGroupingDto("period", ReportTimeGrain.Month, IncludeDetails: true, IncludeEmpty: true, IncludeDescendants: true, GroupKey: "month"),
                new ReportGroupingDto("group", GroupKey: null)
            ],
            ColumnGroups:
            [
                new ReportGroupingDto("group", IncludeDetails: false, IncludeEmpty: false, IncludeDescendants: false, GroupKey: "column")
            ],
            DetailFields: ["document_display"],
            Measures:
            [
                new ReportMeasureSelectionDto("amount", FormatOverride: null),
                new ReportMeasureSelectionDto("amount", ReportAggregationKind.Min, FormatOverride: "0.00")
            ],
            Sorts:
            [
                new ReportSortDto("period", TimeGrain: ReportTimeGrain.Month, GroupKey: "month"),
                new ReportSortDto("group", AppliesToColumnAxis: true, GroupKey: "column"),
                new ReportSortDto("amount", ReportSortDirection.Desc)
            ],
            ShowDetails: true,
            ShowSubtotals: false,
            ShowSubtotalsOnSeparateRows: true,
            ShowGrandTotals: false);
        var wrongCursor = RenderedSheetCursorCodec.EncodeSnapshot(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            0,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        var action = () => fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: layout,
                Filters: new Dictionary<string, ReportFilterValueDto>
                {
                    ["group"] = new(JsonSerializer.SerializeToElement("A"))
                },
                Parameters: new Dictionary<string, string>
                {
                    ["mode"] = "full"
                },
                Limit: 1,
                Cursor: wrongCursor),
            default);

        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*Cursor does not match*");

        var columnOnly = () => fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(
                    ColumnGroups: [new ReportGroupingDto("group")],
                    Measures: [new ReportMeasureSelectionDto("amount")]),
                Limit: 1,
                Cursor: wrongCursor),
            default);
        var subtotalsOnly = () => fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(ShowSubtotals: true),
                Limit: 1,
                Cursor: wrongCursor),
            default);
        var grandTotalOnly = () => fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(ShowGrandTotals: true),
                Limit: 1,
                Cursor: wrongCursor),
            default);
        await columnOnly.Should().ThrowAsync<NgbArgumentInvalidException>();
        await subtotalsOnly.Should().ThrowAsync<NgbArgumentInvalidException>();
        await grandTotalOnly.Should().ThrowAsync<NgbArgumentInvalidException>();
        fixture.Executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_CacheMissWrongReportWrongFingerprintAndValidHit_CoverEverySnapshotCondition()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        fixture.Executor.Page = DataPage(Row("A", 10m), Row("B", 20m), Row("C", 30m));

        var first = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: 1),
            default);
        var original = fixture.Store.LastSet!;
        var cursor = first.NextCursor!;

        fixture.Store.GetOverride = _ => null;
        await fixture.Sut.ExecuteAsync(definition.ReportCode, new ReportExecutionRequestDto(Limit: 1, Cursor: cursor), default);

        fixture.Store.GetOverride = _ => original with { ReportCode = "other.report" };
        await fixture.Sut.ExecuteAsync(definition.ReportCode, new ReportExecutionRequestDto(Limit: 1, Cursor: cursor), default);

        fixture.Store.GetOverride = _ => original with { Fingerprint = Guid.CreateVersion7() };
        await fixture.Sut.ExecuteAsync(definition.ReportCode, new ReportExecutionRequestDto(Limit: 1, Cursor: cursor), default);

        fixture.Store.GetOverride = _ => original with { Diagnostics = null };
        var finalCursor = RenderedSheetCursorCodec.EncodeSnapshot(
            original.SnapshotId,
            original.TotalContentRows - 1,
            original.Fingerprint);
        var cached = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: 1, Cursor: finalCursor),
            default);

        fixture.Executor.CallCount.Should().Be(4);
        cached.HasMore.Should().BeFalse();
        fixture.Store.RemoveCalls.Should().BeGreaterThan(0);
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

    [Fact]
    public async Task Execute_PrebuiltSemanticGrandTotals_AreDeferredAndDiagnosticsAreMerged()
    {
        var definition = Definition();
        var fixture = new EngineFixture(definition);
        var columns = new[] { new ReportSheetColumnDto("group", "Group", "string") };
        fixture.Executor.Page = new ReportDataPage(
            Columns: [],
            Rows: [],
            Offset: 0,
            Limit: 1,
            Total: 1,
            HasMore: false,
            Diagnostics: new Dictionary<string, string> { ["source"] = "page" },
            PrebuiltSheet: new ReportSheetDto(
                columns,
                [
                    SheetRow("normal", semanticRole: "detail"),
                    SheetRow("normal 2", semanticRole: "detail"),
                    SheetRow("hyphen", semanticRole: "grand-total"),
                    SheetRow("underscore", semanticRole: "grand_total")
                ],
                new ReportSheetMetaDto("Prebuilt", Diagnostics: new Dictionary<string, string>
                {
                    ["source"] = "sheet",
                    ["sheetOnly"] = "yes"
                })));

        var result = await fixture.Sut.ExecuteAsync(
            definition.ReportCode,
            new ReportExecutionRequestDto(Limit: 1),
            default);

        result.Sheet.Rows.Should().ContainSingle(x => x.Cells[0].Display == "normal");
        fixture.Store.LastSet!.GrandTotalRow!.Cells[0].Display.Should().Be("underscore");
        fixture.Store.LastSet.Diagnostics!["source"].Should().Be("page");
        fixture.Store.LastSet.Diagnostics["sheetOnly"].Should().Be("yes");
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
        public SnapshotStore Store { get; } = new();
        public ReportEngine Sut { get; }

        public EngineFixture(ReportDefinitionDto definition, IDocumentDisplayReader? displayReader = null)
        {
            Sut = new ReportEngine(
                new DefinitionProvider(definition),
                new NoopValidator(),
                new ReportExecutionPlanner(),
                Executor,
                new ReportSheetBuilder(),
                documentDisplayReader: displayReader,
                renderedReportSnapshotStore: Store);
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

    private sealed class SnapshotStore : IRenderedReportSnapshotStore
    {
        private readonly Dictionary<Guid, RenderedReportSnapshot> _items = [];

        public bool SetResult { get; set; } = true;
        public Func<Guid, RenderedReportSnapshot?>? GetOverride { get; set; }
        public RenderedReportSnapshot? LastSet { get; private set; }
        public int RemoveCalls { get; private set; }

        public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
            => Task.FromResult(GetOverride is null
                ? _items.GetValueOrDefault(snapshotId)
                : GetOverride(snapshotId));

        public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct)
        {
            LastSet = snapshot;
            if (SetResult)
                _items[snapshot.SnapshotId] = snapshot;
            return Task.FromResult(SetResult);
        }

        public Task RemoveAsync(Guid snapshotId, CancellationToken ct)
        {
            RemoveCalls++;
            _items.Remove(snapshotId);
            return Task.CompletedTask;
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
