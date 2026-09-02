using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Reporting.Exceptions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Reporting;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Runtime.Reporting;
using NGB.Trade.Runtime.Tests.Infrastructure;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class TradeCanonicalExecutorsFullCoverageTests
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");
    private static readonly TimeProvider Clock = new TestTimeProvider(new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SalesByItem_CoversFilteringPagingDetailsTotalsAndTotalsDisabled()
    {
        var analytics = new AnalyticsStub
        {
            SalesByItem =
            [
                new(Guid.CreateVersion7(), "Sold", 2m, 20m, 0m, 0m, 20m, 8m),
                new(Guid.CreateVersion7(), "Returned", 0m, 0m, 1m, 20m, -20m, -8m),
                new(Guid.CreateVersion7(), "Empty", 0m, 0m, 0m, 0m, 0m, 0m)
            ]
        };
        var sut = new SalesByItemCanonicalReportExecutor(analytics, Clock);
        sut.ReportCode.Should().Be(TradeCodes.SalesByItemReport);

        var first = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Offset: -1, Limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(
            Definition(sut.ReportCode),
            new ReportExecutionRequestDto(Cursor: first.NextCursor, Limit: 1),
            default);
        var second = await sut.ExecuteAsync(
            Definition(sut.ReportCode),
            new ReportExecutionRequestDto(Layout: new ReportLayoutDto(ShowGrandTotals: false), DisablePaging: true),
            default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(DisablePaging: true), default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 0), default);
        await new SalesByItemCanonicalReportExecutor(
                new AnalyticsStub
                {
                    SalesByItem = [new(Guid.CreateVersion7(), "Non-zero total", 1m, 10m, 0m, 0m, 10m, 4m)]
                }, Clock)
            .ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(), default);

        first.Total.Should().Be(2);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        cursorPage.Offset.Should().Be(1);
        cursorPage.Total.Should().Be(2);
        first.PrebuiltSheet!.Rows.Should().HaveCount(2);
        second.PrebuiltSheet!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task SalesByCustomer_CoversFilteringPagingDetailsTotalsAndTotalsDisabled()
    {
        var analytics = new AnalyticsStub
        {
            SalesByCustomer =
            [
                new(Guid.CreateVersion7(), "Sales", 1, 0, 20m, 0m, 20m, 8m),
                new(Guid.CreateVersion7(), "Returns", 0, 1, 0m, 20m, -20m, -8m),
                new(Guid.CreateVersion7(), "Empty", 0, 0, 0m, 0m, 0m, 0m)
            ]
        };
        var sut = new SalesByCustomerCanonicalReportExecutor(analytics, Clock);
        sut.ReportCode.Should().Be(TradeCodes.SalesByCustomerReport);

        var first = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 0), default);
        var second = await sut.ExecuteAsync(
            Definition(sut.ReportCode),
            new ReportExecutionRequestDto(Layout: new ReportLayoutDto(ShowGrandTotals: false), DisablePaging: true),
            default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(DisablePaging: true), default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 1), default);
        await new SalesByCustomerCanonicalReportExecutor(
                new AnalyticsStub
                {
                    SalesByCustomer = [new(Guid.CreateVersion7(), "Non-zero total", 1, 0, 10m, 0m, 10m, 4m)]
                }, Clock)
            .ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(), default);

        first.PrebuiltSheet!.Rows.Should().HaveCount(3);
        second.PrebuiltSheet!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task PurchasesByVendor_CoversFilteringPagingDetailsTotalsAndTotalsDisabled()
    {
        var analytics = new AnalyticsStub
        {
            PurchasesByVendor =
            [
                new(Guid.CreateVersion7(), "Purchases", 1, 0, 20m, 0m, 20m),
                new(Guid.CreateVersion7(), "Returns", 0, 1, 0m, 5m, -5m),
                new(Guid.CreateVersion7(), "Empty", 0, 0, 0m, 0m, 0m)
            ]
        };
        var sut = new PurchasesByVendorCanonicalReportExecutor(analytics, Clock);
        sut.ReportCode.Should().Be(TradeCodes.PurchasesByVendorReport);

        var first = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 0), default);
        var second = await sut.ExecuteAsync(
            Definition(sut.ReportCode),
            new ReportExecutionRequestDto(Layout: new ReportLayoutDto(ShowGrandTotals: false), DisablePaging: true),
            default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 1), default);

        first.PrebuiltSheet!.Rows.Should().HaveCount(3);
        second.PrebuiltSheet!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dashboard_CoversPopulatedAndEmptySectionsAndAllRowFactories()
    {
        var policy = Policy();
        var item = Guid.CreateVersion7();
        var warehouse = Guid.CreateVersion7();
        var populated = new AnalyticsStub
        {
            SalesByItem =
            [
                new(item, "Top", 2m, 20m, 0m, 0m, 20m, 8m),
                new(Guid.CreateVersion7(), "Return only", 0m, 0m, 1m, 2m, -2m, -1m),
                new(Guid.CreateVersion7(), "Empty", 0m, 0m, 0m, 0m, 0m, 0m)
            ],
            SalesByCustomer =
            [
                new(Guid.CreateVersion7(), "Customer", 1, 0, 20m, 0m, 20m, 8m)
            ],
            PurchasesByVendor = [new(Guid.CreateVersion7(), "Vendor", 1, 0, 10m, 0m, 10m)],
            RecentDocuments =
            [
                new(Guid.CreateVersion7(), TradeCodes.SalesInvoice, "Sales Invoice", "SI-1", new DateOnly(2026, 4, 10),
                    DateTime.UtcNow, "Posted", "Customer", 20m),
                new(Guid.CreateVersion7(), TradeCodes.PurchaseReceipt, "Purchase Receipt", "PR-1", new DateOnly(2026, 4, 11),
                    DateTime.UtcNow, "Posted", null, null)
            ]
        };
        var balances = new InventoryBalanceStub(
        [
            new(item, "Top", warehouse, "Main", 5m),
            new(Guid.CreateVersion7(), "Second", Guid.CreateVersion7(), "Overflow", 2m)
        ]);
        var sut = new TradeDashboardOverviewCanonicalReportExecutor(
            populated, PolicyReader(policy), balances, Clock);
        sut.ReportCode.Should().Be(TradeCodes.DashboardOverviewReport);

        var page = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(), default);

        page.PrebuiltSheet!.Rows.Should().NotBeEmpty();

        var empty = new TradeDashboardOverviewCanonicalReportExecutor(
            new AnalyticsStub(), PolicyReader(policy), new InventoryBalanceStub([]), Clock);
        var emptyPage = await empty.ExecuteAsync(
            Definition(empty.ReportCode),
            new ReportExecutionRequestDto(Parameters: new Dictionary<string, string> { ["as_of_utc"] = "2026-04-18" }),
            default);
        emptyPage.PrebuiltSheet!.Rows.Count(row => row.SemanticRole == "section_header").Should().Be(6);
    }

    [Fact]
    public async Task InventoryBalances_CoversZeroFilteringActionsAndPagingBranches()
    {
        var policy = Policy();
        var item = Guid.CreateVersion7();
        var warehouse = Guid.CreateVersion7();
        var reader = new InventoryBalanceStub(
        [
            new(item, "Item A", warehouse, "WH A", 5m),
            new(Guid.CreateVersion7(), "Item B", Guid.CreateVersion7(), "WH B", 3m),
            new(Guid.CreateVersion7(), "Item C", Guid.CreateVersion7(), "WH C", 2m)
        ]);
        var sut = new InventoryBalancesCanonicalReportExecutor(
            PolicyReader(policy), reader, Clock);
        sut.ReportCode.Should().Be(TradeCodes.InventoryBalancesReport);

        var first = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Offset: -5, Limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(
            Definition(sut.ReportCode),
            new ReportExecutionRequestDto(Cursor: first.NextCursor, Limit: 1),
            default);
        var second = await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(DisablePaging: true), default);
        await sut.ExecuteAsync(Definition(sut.ReportCode), new ReportExecutionRequestDto(Limit: 0), default);

        first.Total.Should().Be(3);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        cursorPage.Offset.Should().Be(1);
        cursorPage.Total.Should().Be(3);
        second.PrebuiltSheet!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task InventoryMovements_CoversRangeValidationFilteringDocumentsStornoAndActions()
    {
        var policy = Policy();
        var documentId = Guid.CreateVersion7();
        var missingDocumentId = Guid.CreateVersion7();
        var movementRows = new[]
        {
            Movement(1, documentId, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), true, true),
            Movement(2, missingDocumentId, new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc), false, false),
            Movement(3, Guid.CreateVersion7(), new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc), false, true)
        };
        var movements = MovementReader(movementRows);
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == documentId
                ? new DocumentRecord
                {
                    Id = id, TypeCode = TradeCodes.SalesInvoice, Number = "SI-1", DateUtc = DateTime.UtcNow,
                    Status = DocumentStatus.Posted
                }
                : null);
        documents.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids
                    .Where(id => id == documentId)
                    .Distinct()
                    .ToDictionary(
                        static id => id,
                        static id => new DocumentRecord
                        {
                            Id = id,
                            TypeCode = TradeCodes.SalesInvoice,
                            Number = "SI-1",
                            DateUtc = DateTime.UtcNow,
                            Status = DocumentStatus.Posted
                        }));
        var sut = new InventoryMovementsCanonicalReportExecutor(PolicyReader(policy), movements.Object, documents.Object, Clock);
        sut.ReportCode.Should().Be(TradeCodes.InventoryMovementsReport);
        var definition = Definition(sut.ReportCode);

        var invalid = () => sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-04-20", ["to_utc"] = "2026-04-01" }), default);
        await invalid.Should().ThrowAsync<ReportLayoutValidationException>();

        var page = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-04-01", ["to_utc"] = "2026-04-30" },
            Offset: -1, Limit: 1), default);
        var all = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-04-01", ["to_utc"] = "2026-04-30" },
            DisablePaging: true), default);
        var next = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-04-01", ["to_utc"] = "2026-04-30" },
            Cursor: page.NextCursor,
            Limit: 1), default);
        var legacy = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-04-01", ["to_utc"] = "2026-04-30" },
            Offset: 1,
            Limit: 1), default);
        await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Limit: 0), default);
        await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["item_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(Guid.CreateVersion7()))
            }), default);

        page.Total.Should().BeNull();
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().NotBeNullOrWhiteSpace();
        next.HasMore.Should().BeFalse();
        legacy.Total.Should().Be(2);
        all.PrebuiltSheet!.Rows.Should().HaveCount(2);
    }

    private static ReportDefinitionDto Definition(string code) =>
        new TradeCanonicalReportDefinitionSource().GetDefinitions().Single(definition => definition.ReportCode == code);

    private static TradeAccountingPolicy Policy() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());

    private static ITradeAccountingPolicyReader PolicyReader(TradeAccountingPolicy policy)
    {
        var reader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        return reader.Object;
    }

    private static Mock<IOperationalRegisterReadService> ProjectionReader(
        params OperationalRegisterMonthlyProjectionReadRow[] rows)
    {
        var reader = new Mock<IOperationalRegisterReadService>(MockBehavior.Strict);
        reader.Setup(x => x.GetBalancesPageAsync(
                It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
                new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId, request.FromInclusive, request.ToInclusive, rows, false, null));
        return reader;
    }

    private static Mock<IOperationalRegisterMovementsQueryReader> EmptyMovements() => MovementReader([]);

    private static Mock<IOperationalRegisterMovementsQueryReader> MovementReader(
        IReadOnlyList<OperationalRegisterMovementQueryReadRow> rows)
    {
        var reader = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetByOccurredAtCursorAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                It.IsAny<OperationalRegisterOccurredAtCursor?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Guid _,
                DateOnly from,
                DateOnly to,
                IReadOnlyList<DimensionValue>? _,
                OperationalRegisterOccurredAtCursor? cursor,
                int limit,
                CancellationToken _) => rows
                    .Where(row => DateOnly.FromDateTime(row.OccurredAtUtc) >= from
                                  && DateOnly.FromDateTime(row.OccurredAtUtc) <= to)
                    .Where(row => cursor is null
                                  || row.OccurredAtUtc > cursor.AfterOccurredAtUtc
                                  || (row.OccurredAtUtc == cursor.AfterOccurredAtUtc
                                      && row.MovementId > cursor.AfterMovementId))
                    .OrderBy(static row => row.OccurredAtUtc)
                    .ThenBy(static row => row.MovementId)
                    .Take(limit)
                    .ToArray());
        reader.Setup(x => x.GetByOccurredAtPageAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Guid _,
                DateOnly from,
                DateOnly to,
                IReadOnlyList<DimensionValue>? _,
                int offset,
                int? limit,
                CancellationToken _) =>
            {
                var filtered = rows
                    .Where(row => DateOnly.FromDateTime(row.OccurredAtUtc) >= from
                                  && DateOnly.FromDateTime(row.OccurredAtUtc) <= to)
                    .OrderBy(static row => row.OccurredAtUtc)
                    .ThenBy(static row => row.MovementId)
                    .ToArray();
                var pageRows = limit.HasValue
                    ? filtered.Skip(offset).Take(limit.Value).ToArray()
                    : filtered.Skip(offset).ToArray();
                return new OperationalRegisterMovementQueryPage(pageRows, filtered.Length);
            });
        reader.Setup(x => x.GetByMonthsAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>?>(), null, null, null, null, 1000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        return reader;
    }

    private static OperationalRegisterMonthlyProjectionReadRow Balance(
        Guid dimensionSetId, Guid itemId, Guid warehouseId, decimal quantity, bool withDimensions)
    {
        var dimensionValues = new List<DimensionValue>();
        if (withDimensions && itemId != Guid.Empty)
            dimensionValues.Add(new DimensionValue(ItemDimensionId, itemId));
        if (withDimensions && warehouseId != Guid.Empty)
            dimensionValues.Add(new DimensionValue(WarehouseDimensionId, warehouseId));
        var dimensions = dimensionValues.Count == 0 ? DimensionBag.Empty : new DimensionBag(dimensionValues);
        var displays = new Dictionary<Guid, string>();
        if (itemId != Guid.Empty)
            displays[ItemDimensionId] = $"Item {itemId:N}";
        if (warehouseId != Guid.Empty)
            displays[WarehouseDimensionId] = $"WH {warehouseId:N}";
        return new OperationalRegisterMonthlyProjectionReadRow
        {
            PeriodMonth = new DateOnly(2026, 3, 1), DimensionSetId = dimensionSetId, Dimensions = dimensions,
            DimensionValueDisplays = displays,
            Values = new Dictionary<string, decimal> { ["qty_delta"] = quantity }
        };
    }

    private static OperationalRegisterMovementQueryReadRow Movement(
        long id, Guid documentId, DateTime occurred, bool storno, bool withDimensions)
    {
        var item = Guid.CreateVersion7();
        var warehouse = Guid.CreateVersion7();
        return new OperationalRegisterMovementQueryReadRow
        {
            MovementId = id, DocumentId = documentId, OccurredAtUtc = occurred,
            PeriodMonth = new DateOnly(occurred.Year, occurred.Month, 1), DimensionSetId = Guid.CreateVersion7(),
            IsStorno = storno,
            Dimensions = withDimensions
                ? new DimensionBag([new DimensionValue(ItemDimensionId, item), new DimensionValue(WarehouseDimensionId, warehouse)])
                : DimensionBag.Empty,
            DimensionValueDisplays = withDimensions
                ? new Dictionary<Guid, string> { [ItemDimensionId] = "Item", [WarehouseDimensionId] = "Warehouse" }
                : new Dictionary<Guid, string>(),
            Values = new Dictionary<string, decimal> { ["qty_in"] = 2m, ["qty_out"] = 1m, ["qty_delta"] = 1m }
        };
    }

    private sealed class AnalyticsStub : ITradeAnalyticsReader
    {
        public IReadOnlyList<SalesByItemSummaryRow> SalesByItem { get; init; } = [];
        public IReadOnlyList<SalesByCustomerSummaryRow> SalesByCustomer { get; init; } = [];
        public IReadOnlyList<PurchasesByVendorSummaryRow> PurchasesByVendor { get; init; } = [];
        public IReadOnlyList<RecentTradeDocumentSummaryRow> RecentDocuments { get; init; } = [];
        public Task<TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>> GetSalesByItemPageAsync(
            DateOnly fromInclusive, DateOnly toInclusive, IReadOnlyList<Guid>? itemIds,
            IReadOnlyList<Guid>? customerIds, IReadOnlyList<Guid>? warehouseIds, int offset, int limit,
            CancellationToken ct = default)
        {
            var rows = SalesByItem.Where(static row => row.SoldQuantity != 0m || row.ReturnedQuantity != 0m).ToArray();
            return Task.FromResult(new TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>(
                rows.Skip(offset).Take(limit).ToArray(),
                rows.Length,
                new SalesByItemTotals(
                    rows.Sum(static row => row.SoldQuantity),
                    rows.Sum(static row => row.GrossSales),
                    rows.Sum(static row => row.ReturnedQuantity),
                    rows.Sum(static row => row.ReturnedAmount),
                    rows.Sum(static row => row.NetSales),
                    rows.Sum(static row => row.NetCogs))));
        }
        public Task<IReadOnlyList<SalesByItemSummaryRow>> GetSalesByItemAsync(DateOnly fromInclusive, DateOnly toInclusive,
            IReadOnlyList<Guid>? itemIds, IReadOnlyList<Guid>? customerIds, IReadOnlyList<Guid>? warehouseIds,
            CancellationToken ct = default) => Task.FromResult(SalesByItem);
        public Task<TradeAnalyticsPage<SalesByCustomerSummaryRow, SalesByCustomerTotals>> GetSalesByCustomerPageAsync(
            DateOnly fromInclusive, DateOnly toInclusive, IReadOnlyList<Guid>? customerIds,
            IReadOnlyList<Guid>? itemIds, IReadOnlyList<Guid>? warehouseIds, int offset, int limit,
            CancellationToken ct = default)
        {
            var rows = SalesByCustomer.Where(static row => row.SalesDocumentCount != 0 || row.ReturnDocumentCount != 0).ToArray();
            return Task.FromResult(new TradeAnalyticsPage<SalesByCustomerSummaryRow, SalesByCustomerTotals>(
                rows.Skip(offset).Take(limit).ToArray(),
                rows.Length,
                new SalesByCustomerTotals(
                    rows.Sum(static row => row.SalesDocumentCount),
                    rows.Sum(static row => row.ReturnDocumentCount),
                    rows.Sum(static row => row.GrossSales),
                    rows.Sum(static row => row.ReturnedAmount),
                    rows.Sum(static row => row.NetSales),
                    rows.Sum(static row => row.NetCogs))));
        }
        public Task<IReadOnlyList<SalesByCustomerSummaryRow>> GetSalesByCustomerAsync(DateOnly fromInclusive,
            DateOnly toInclusive, IReadOnlyList<Guid>? customerIds, IReadOnlyList<Guid>? itemIds,
            IReadOnlyList<Guid>? warehouseIds, CancellationToken ct = default) => Task.FromResult(SalesByCustomer);
        public Task<TradeAnalyticsPage<PurchasesByVendorSummaryRow, PurchasesByVendorTotals>> GetPurchasesByVendorPageAsync(
            DateOnly fromInclusive, DateOnly toInclusive, IReadOnlyList<Guid>? vendorIds,
            IReadOnlyList<Guid>? itemIds, IReadOnlyList<Guid>? warehouseIds, int offset, int limit,
            CancellationToken ct = default)
        {
            var rows = PurchasesByVendor.Where(static row => row.PurchaseDocumentCount != 0 || row.ReturnDocumentCount != 0).ToArray();
            return Task.FromResult(new TradeAnalyticsPage<PurchasesByVendorSummaryRow, PurchasesByVendorTotals>(
                rows.Skip(offset).Take(limit).ToArray(),
                rows.Length,
                new PurchasesByVendorTotals(
                    rows.Sum(static row => row.PurchaseDocumentCount),
                    rows.Sum(static row => row.ReturnDocumentCount),
                    rows.Sum(static row => row.GrossPurchases),
                    rows.Sum(static row => row.ReturnedAmount),
                    rows.Sum(static row => row.NetPurchases))));
        }
        public Task<IReadOnlyList<PurchasesByVendorSummaryRow>> GetPurchasesByVendorAsync(DateOnly fromInclusive,
            DateOnly toInclusive, IReadOnlyList<Guid>? vendorIds, IReadOnlyList<Guid>? itemIds,
            IReadOnlyList<Guid>? warehouseIds, CancellationToken ct = default) => Task.FromResult(PurchasesByVendor);
        public Task<IReadOnlyList<RecentTradeDocumentSummaryRow>> GetRecentDocumentsAsync(DateOnly asOf, int limit,
            CancellationToken ct = default) => Task.FromResult(RecentDocuments);
    }

    private sealed class InventoryBalanceStub(IReadOnlyList<TradeInventoryBalanceRow> rows) : ITradeInventoryBalanceReader
    {
        public Task<TradeInventoryBalancePage> GetPageAsync(
            Guid registerId,
            DateOnly asOfInclusive,
            IReadOnlyList<Guid>? itemIds,
            IReadOnlyList<Guid>? warehouseIds,
            TradeInventoryBalanceSort sort,
            int offset,
            int limit,
            CancellationToken ct = default)
        {
            IEnumerable<TradeInventoryBalanceRow> filtered = rows.Where(static row => row.Quantity != 0m);
            if (itemIds is { Count: > 0 })
                filtered = filtered.Where(row => itemIds.Contains(row.ItemId));
            if (warehouseIds is { Count: > 0 })
                filtered = filtered.Where(row => warehouseIds.Contains(row.WarehouseId));

            var materialized = (sort == TradeInventoryBalanceSort.AbsoluteQuantityDescending
                    ? filtered.OrderByDescending(static row => Math.Abs(row.Quantity))
                    : filtered.OrderBy(static row => row.ItemDisplay).ThenBy(static row => row.WarehouseDisplay))
                .ToArray();
            return Task.FromResult(new TradeInventoryBalancePage(
                materialized.Skip(offset).Take(limit).ToArray(),
                materialized.Length,
                materialized.Sum(static row => row.Quantity)));
        }
    }
}
