using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Datasets;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngine_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Materializes_Sheet_From_Executor_Page()
    {
        var executor = new StubPlanExecutor();
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            executor,
            new ReportSheetBuilder());

        var response = await sut.ExecuteAsync(
            AccountingLedgerAnalysisDatasetModel.DatasetCode,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        response.Sheet.Columns.Should().HaveCount(2);
        response.Sheet.Rows.Should().HaveCount(2);
        response.Sheet.Rows[0].RowKind.Should().Be(ReportRowKind.Group);
        response.Sheet.Rows[1].RowKind.Should().Be(ReportRowKind.Total);
        response.Sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        response.Sheet.Rows[0].Cells[1].Display.Should().Be("125.50");
        response.Sheet.Rows[1].Cells[0].Display.Should().Be("Total");
        response.Sheet.Rows[1].Cells[1].Display.Should().Be("125.50");
        response.Diagnostics.Should().ContainKey("executor");
        response.Diagnostics!["executor"].Should().Be("stub-plan-executor");
    }

    private sealed class StubDefinitionSource : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
            => [new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single()];
    }

    private sealed class StubPlanExecutor : IReportPlanExecutor
    {
        public ReportExecutionRequestDto? LastRequest { get; private set; }

        public Task<ReportDataPage> ExecuteAsync(ReportDefinitionDto definition, ReportExecutionRequestDto request, string reportCode, string? datasetCode, IReadOnlyList<ReportPlanGrouping> rowGroups, IReadOnlyList<ReportPlanGrouping> columnGroups, IReadOnlyList<ReportPlanFieldSelection> detailFields, IReadOnlyList<ReportPlanMeasure> measures, IReadOnlyList<ReportPlanSort> sorts, IReadOnlyList<ReportPlanPredicate> predicates, IReadOnlyList<ReportPlanParameter> parameters, ReportPlanPaging paging, CancellationToken ct)
        {
            LastRequest = request;

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_display"] = "1100 — Accounts Receivable",
                ["debit_amount__sum"] = 125.50m
            };

            return Task.FromResult(new ReportDataPage(
                Columns:
                [
                    new ReportDataColumn("account_display", "Account", "string", "row-group"),
                    new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
                ],
                Rows: [new ReportDataRow(values)],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 1,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "stub-plan-executor"
                }));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Enriches_Document_Display_And_Attaches_Document_Action_For_Composable_Report()
    {
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            new StubDocumentPlanExecutor(),
            new ReportSheetBuilder(),
            documentDisplayReader: new StubDocumentDisplayReader());

        var response = await sut.ExecuteAsync(
            AccountingLedgerAnalysisDatasetModel.DatasetCode,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Layout: new ReportLayoutDto(
                    DetailFields: ["document_display"],
                    ShowDetails: true),
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        response.Sheet.Rows.Should().HaveCount(1);
        response.Sheet.Rows[0].Cells[0].Display.Should().Be("Receivable Charge RC-2026-000001 2026-03-05");
        response.Sheet.Rows[0].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: "pm.receivable_charge",
            DocumentId: StubDocumentPlanExecutor.DocumentId));
    }

    [Fact]
    public async Task ExecuteAsync_Attaches_Document_Action_For_Aliased_Document_Display_Field()
    {
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubAliasedDocumentDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            new StubAliasedDocumentPlanExecutor(),
            new ReportSheetBuilder());

        var response = await sut.ExecuteAsync(
            StubAliasedDocumentDefinitionSource.ReportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(
                    DetailFields: ["timesheet_display"],
                    ShowDetails: true),
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        response.Sheet.Rows.Should().HaveCount(1);
        response.Sheet.Rows[0].Cells[0].Display.Should().Be("Timesheet T-2026-000001");
        response.Sheet.Rows[0].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: "ab.timesheet",
            DocumentId: StubAliasedDocumentPlanExecutor.DocumentId));
    }

    [Fact]
    public async Task ExecuteExportSheetAsync_Forces_Unpaged_Execution_Request()
    {
        var executor = new StubPlanExecutor();
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            executor,
            new ReportSheetBuilder());

        var sheet = await sut.ExecuteExportSheetAsync(
            AccountingLedgerAnalysisDatasetModel.DatasetCode,
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true)),
            CancellationToken.None);

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.DisablePaging.Should().BeTrue();
        sheet.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_ComposableGroupedPaging_Paginates_RenderedRows_And_Defers_GrandTotal_To_Final_Page()
    {
        var executor = new StubGroupedComposablePlanExecutor();
        var snapshots = new StubRenderedReportSnapshotStore();
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubComposableDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            executor,
            new ReportSheetBuilder(),
            renderedReportSnapshotStore: snapshots);

        var firstPage = await sut.ExecuteAsync(
            StubComposableDefinitionSource.ReportCode,
            new ReportExecutionRequestDto(Limit: 4),
            CancellationToken.None);

        firstPage.Total.Should().Be(5);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage.Sheet.Rows.Should().HaveCount(4);
        firstPage.Sheet.Rows.Should().NotContain(row => row.RowKind == ReportRowKind.Total);
        firstPage.Sheet.Rows[0].Cells[0].Display.Should().Be("Florida Fulfillment Center");
        firstPage.Sheet.Rows[3].Cells[0].Display.Should().Be("Texas Distribution Hub");

        var secondPage = await sut.ExecuteAsync(
            StubComposableDefinitionSource.ReportCode,
            new ReportExecutionRequestDto(Limit: 4, Cursor: firstPage.NextCursor),
            CancellationToken.None);

        executor.Requests.Should().HaveCount(1);
        executor.Requests.Should().OnlyContain(request => request.DisablePaging);
        snapshots.SetCalls.Should().Be(1);
        snapshots.GetCalls.Should().Be(1);
        secondPage.Total.Should().Be(5);
        secondPage.HasMore.Should().BeFalse();
        secondPage.NextCursor.Should().BeNull();
        secondPage.Sheet.Rows.Should().HaveCount(2);
        secondPage.Sheet.Rows[0].Cells[0].Display.Should().Be("Widget Gamma");
        secondPage.Sheet.Rows[1].RowKind.Should().Be(ReportRowKind.Total);
        secondPage.Sheet.Rows[1].Cells[0].Display.Should().Be("Total");
        secondPage.Sheet.Rows[1].Cells[1].Display.Should().Be("45");
    }

    private sealed class StubDocumentPlanExecutor : IReportPlanExecutor
    {
        public static readonly Guid DocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public Task<ReportDataPage> ExecuteAsync(ReportDefinitionDto definition, ReportExecutionRequestDto request, string reportCode, string? datasetCode, IReadOnlyList<ReportPlanGrouping> rowGroups, IReadOnlyList<ReportPlanGrouping> columnGroups, IReadOnlyList<ReportPlanFieldSelection> detailFields, IReadOnlyList<ReportPlanMeasure> measures, IReadOnlyList<ReportPlanSort> sorts, IReadOnlyList<ReportPlanPredicate> predicates, IReadOnlyList<ReportPlanParameter> parameters, ReportPlanPaging paging, CancellationToken ct)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["document_display"] = "RC-2026-000001",
                [ReportInteractiveSupport.SupportDocumentId] = DocumentId
            };

            return Task.FromResult(new ReportDataPage(
                Columns:
                [
                    new ReportDataColumn("document_display", "Document", "string", "detail"),
                    new ReportDataColumn(ReportInteractiveSupport.SupportDocumentId, ReportInteractiveSupport.SupportDocumentId, "uuid", "support")
                ],
                Rows: [new ReportDataRow(values)],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 1,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "stub-document-plan-executor"
                }));
        }
    }

    private sealed class StubDocumentDisplayReader : IDocumentDisplayReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
            {
                [StubDocumentPlanExecutor.DocumentId] = "Receivable Charge RC-2026-000001 2026-03-05"
            });

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, DocumentDisplayRef>>(new Dictionary<Guid, DocumentDisplayRef>
            {
                [StubDocumentPlanExecutor.DocumentId] = new(StubDocumentPlanExecutor.DocumentId, "pm.receivable_charge", "Receivable Charge RC-2026-000001 2026-03-05")
            });
    }

    private sealed class StubAliasedDocumentDefinitionSource : IReportDefinitionSource
    {
        public const string ReportCode = "test.agency_billing_unbilled_time";

        public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
            => [
                new ReportDefinitionDto(
                    ReportCode: ReportCode,
                    Name: "Agency Billing Unbilled Time",
                    Mode: ReportExecutionMode.Composable,
                    Dataset: new ReportDatasetDto(
                        DatasetCode: ReportCode,
                        Fields:
                        [
                            new ReportFieldDto(
                                "timesheet_id",
                                "Timesheet",
                                "uuid",
                                ReportFieldKind.Dimension,
                                IsFilterable: true,
                                Lookup: new DocumentLookupSourceDto(["ab.timesheet"])),
                            new ReportFieldDto(
                                "timesheet_display",
                                "Timesheet",
                                "string",
                                ReportFieldKind.Detail,
                                IsGroupable: true,
                                IsSortable: true,
                                IsSelectable: true)
                        ],
                        Measures: []),
                    Capabilities: new ReportCapabilitiesDto(
                        AllowsFilters: false,
                        AllowsRowGroups: true,
                        AllowsColumnGroups: false,
                        AllowsMeasures: true,
                        AllowsDetailFields: true,
                        AllowsSorting: true,
                        AllowsShowDetails: true,
                        AllowsSubtotals: true,
                        AllowsSeparateRowSubtotals: true,
                        AllowsGrandTotals: true,
                        AllowsVariants: false,
                        AllowsXlsxExport: true,
                        MaxRowGroupDepth: 2,
                        MaxVisibleColumns: 8,
                        MaxVisibleRows: 100,
                        MaxRenderedCells: 800),
                    DefaultLayout: new ReportLayoutDto(
                        DetailFields: ["timesheet_display"],
                        ShowDetails: true),
                    Presentation: new ReportPresentationDto(
                        InitialPageSize: 50,
                        RowNoun: "rows"))
            ];
    }

    private sealed class StubAliasedDocumentPlanExecutor : IReportPlanExecutor
    {
        public static readonly Guid DocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

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
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["timesheet_display"] = "Timesheet T-2026-000001",
                ["timesheet_id"] = DocumentId
            };

            return Task.FromResult(new ReportDataPage(
                Columns:
                [
                    new ReportDataColumn("timesheet_display", "Timesheet", "string", "detail"),
                    new ReportDataColumn("timesheet_id", "Timesheet", "uuid", "dimension")
                ],
                Rows: [new ReportDataRow(values)],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 1,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "stub-aliased-document-plan-executor"
                }));
        }
    }

    private sealed class StubComposableDefinitionSource : IReportDefinitionSource
    {
        public const string ReportCode = "test.inventory_balances";

        public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
            => [
                new ReportDefinitionDto(
                    ReportCode: ReportCode,
                    Name: "Inventory Balances",
                    Mode: ReportExecutionMode.Composable,
                    Dataset: new ReportDatasetDto(
                        DatasetCode: ReportCode,
                        Fields:
                        [
                            new ReportFieldDto("warehouse_display", "Warehouse", "string", ReportFieldKind.Dimension, IsGroupable: true, IsSortable: true),
                            new ReportFieldDto("item_display", "Item", "string", ReportFieldKind.Dimension, IsGroupable: true, IsSortable: true)
                        ],
                        Measures:
                        [
                            new ReportMeasureDto("quantity_on_hand", "Quantity On Hand", "decimal", [ReportAggregationKind.Sum])
                        ]),
                    Capabilities: new ReportCapabilitiesDto(
                        AllowsFilters: false,
                        AllowsRowGroups: true,
                        AllowsColumnGroups: false,
                        AllowsMeasures: true,
                        AllowsDetailFields: true,
                        AllowsSorting: true,
                        AllowsShowDetails: true,
                        AllowsSubtotals: true,
                        AllowsSeparateRowSubtotals: true,
                        AllowsGrandTotals: true,
                        AllowsVariants: false,
                        AllowsXlsxExport: true,
                        MaxRowGroupDepth: 4,
                        MaxVisibleColumns: 8,
                        MaxVisibleRows: 100,
                        MaxRenderedCells: 800),
                    DefaultLayout: new ReportLayoutDto(
                        RowGroups:
                        [
                            new ReportGroupingDto("warehouse_display"),
                            new ReportGroupingDto("item_display")
                        ],
                        Measures:
                        [
                            new ReportMeasureSelectionDto("quantity_on_hand")
                        ],
                        Sorts:
                        [
                            new ReportSortDto("warehouse_display"),
                            new ReportSortDto("item_display")
                        ],
                        ShowDetails: false,
                        ShowSubtotals: true,
                        ShowSubtotalsOnSeparateRows: false,
                        ShowGrandTotals: true),
                    Presentation: new ReportPresentationDto(
                        InitialPageSize: 4,
                        RowNoun: "balance rows"))
            ];
    }

    private sealed class StubGroupedComposablePlanExecutor : IReportPlanExecutor
    {
        public List<ReportExecutionRequestDto> Requests { get; } = [];

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
            Requests.Add(request);

            return Task.FromResult(new ReportDataPage(
                Columns:
                [
                    new ReportDataColumn("warehouse_display", "Warehouse", "string", "row-group"),
                    new ReportDataColumn("item_display", "Item", "string", "row-group"),
                    new ReportDataColumn("quantity_on_hand__sum", "Quantity On Hand", "decimal", "measure")
                ],
                Rows:
                [
                    new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["warehouse_display"] = "Florida Fulfillment Center",
                        ["item_display"] = "Widget Alpha",
                        ["quantity_on_hand__sum"] = 10m
                    }),
                    new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["warehouse_display"] = "Florida Fulfillment Center",
                        ["item_display"] = "Widget Beta",
                        ["quantity_on_hand__sum"] = 15m
                    }),
                    new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["warehouse_display"] = "Texas Distribution Hub",
                        ["item_display"] = "Widget Gamma",
                        ["quantity_on_hand__sum"] = 20m
                    })
                ],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 3,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "stub-grouped-composable-plan-executor"
                }));
        }
    }

    private sealed class StubRenderedReportSnapshotStore : IRenderedReportSnapshotStore
    {
        private readonly Dictionary<Guid, RenderedReportSnapshot> _items = new();

        public int SetCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
        {
            GetCalls += 1;
            return Task.FromResult(_items.TryGetValue(snapshotId, out var snapshot) ? snapshot : null);
        }

        public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct)
        {
            SetCalls += 1;
            _items[snapshot.SnapshotId] = snapshot;
            return Task.FromResult(true);
        }

        public Task RemoveAsync(Guid snapshotId, CancellationToken ct)
        {
            _items.Remove(snapshotId);
            return Task.CompletedTask;
        }
    }
}
