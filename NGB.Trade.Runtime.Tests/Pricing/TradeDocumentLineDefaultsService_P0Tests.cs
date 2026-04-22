using FluentAssertions;
using Moq;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Trade.Contracts;
using NGB.Trade.Documents;
using NGB.Trade.Pricing;
using NGB.Trade.Runtime.Pricing;
using NGB.Trade.Runtime.Tests.Infrastructure;

namespace NGB.Trade.Runtime.Tests.Pricing;

public sealed class TradeDocumentLineDefaultsService_P0Tests
{
    [Fact]
    public async Task ResolveAsync_WhenRequestIsNull_Throws()
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync(null!, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task ResolveAsync_WhenDocumentTypeIsUnsupported_Throws()
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: "trd.unsupported",
                AsOfDate: null,
                WarehouseId: null,
                PriceTypeId: null,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows: []),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("documentType");
        ex.Which.Reason.Should().Contain("is not supported");
    }

    [Fact]
    public async Task ResolveAsync_WhenRowsAreInvalid_ReturnsEmpty_WithoutTouchingReaders()
    {
        var pricing = new Mock<ITradePricingLookupReader>(MockBehavior.Strict);
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        var uow = CreateUnitOfWorkMock();
        var sut = CreateSut(pricing.Object, readers.Object, uow.Object);

        var response = await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.SalesInvoice,
                AsOfDate: "2026-04-18",
                WarehouseId: Guid.NewGuid(),
                PriceTypeId: Guid.NewGuid(),
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("", Guid.NewGuid(), null),
                    new TradeDocumentLineDefaultsRowRequestDto("line-2", Guid.Empty, null),
                    new TradeDocumentLineDefaultsRowRequestDto("   ", Guid.Empty, null)
                ]),
            CancellationToken.None);

        response.Rows.Should().BeEmpty();
        pricing.VerifyNoOtherCalls();
        readers.VerifyNoOtherCalls();
        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_SalesInvoice_UsesRequestPriceType_AndDistinctRowKeys()
    {
        var warehouseId = Guid.NewGuid();
        var requestPriceTypeId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var duplicateItemId = Guid.NewGuid();
        var capturedItemIds = Array.Empty<Guid>();
        var capturedPriceKeys = Array.Empty<TradePriceLookupKey>();
        var capturedCostKeys = Array.Empty<TradeWarehouseItemKey>();
        var capturedAsOf = default(DateOnly);

        var pricing = new Mock<ITradePricingLookupReader>();
        pricing
            .Setup(x => x.GetItemSalesProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((itemIds, _) => capturedItemIds = itemIds.ToArray())
            .ReturnsAsync(new Dictionary<Guid, TradeItemSalesProfile>
            {
                [firstItemId] = new(firstItemId, Guid.NewGuid(), "Retail")
            });
        pricing
            .Setup(x => x.GetLatestItemPricesAsync(It.IsAny<IReadOnlyCollection<TradePriceLookupKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<TradePriceLookupKey>, DateOnly, CancellationToken>((keys, asOf, _) =>
            {
                capturedPriceKeys = keys.ToArray();
                capturedAsOf = asOf;
            })
            .ReturnsAsync(new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>
            {
                [new(firstItemId, requestPriceTypeId)] = new(firstItemId, requestPriceTypeId, 14.25m, "CAD", new DateOnly(2026, 4, 9), Guid.NewGuid())
            });
        pricing
            .Setup(x => x.GetLatestUnitCostsAsync(It.IsAny<IReadOnlyCollection<TradeWarehouseItemKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<TradeWarehouseItemKey>, DateOnly, CancellationToken>((keys, _, _) => capturedCostKeys = keys.ToArray())
            .ReturnsAsync(new Dictionary<TradeWarehouseItemKey, decimal>
            {
                [new(warehouseId, firstItemId)] = 6.75m
            });

        var sut = CreateSut(pricing.Object);

        var response = await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.SalesInvoice,
                AsOfDate: "2026-04-09",
                WarehouseId: warehouseId,
                PriceTypeId: requestPriceTypeId,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", firstItemId, null),
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", duplicateItemId, null),
                    new TradeDocumentLineDefaultsRowRequestDto("line-2", Guid.Empty, null)
                ]),
            CancellationToken.None);

        response.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TradeDocumentLineDefaultsRowResultDto(
                RowKey: "line-1",
                PriceType: null,
                UnitPrice: 14.25m,
                Currency: "CAD",
                UnitCost: 6.75m));
        capturedItemIds.Should().Equal(firstItemId);
        capturedPriceKeys.Should().Equal(new TradePriceLookupKey(firstItemId, requestPriceTypeId));
        capturedCostKeys.Should().Equal(new TradeWarehouseItemKey(warehouseId, firstItemId));
        capturedAsOf.Should().Be(new DateOnly(2026, 4, 9));
    }

    [Fact]
    public async Task ResolveAsync_WhenAsOfDateIsInvalid_FallsBackToTimeProvider()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 4, 18, 16, 45, 0, TimeSpan.Zero));
        var itemId = Guid.NewGuid();
        var priceTypeId = Guid.NewGuid();
        var capturedAsOf = default(DateOnly);

        var pricing = new Mock<ITradePricingLookupReader>();
        pricing
            .Setup(x => x.GetItemSalesProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TradeItemSalesProfile>());
        pricing
            .Setup(x => x.GetLatestItemPricesAsync(It.IsAny<IReadOnlyCollection<TradePriceLookupKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<TradePriceLookupKey>, DateOnly, CancellationToken>((_, asOf, _) => capturedAsOf = asOf)
            .ReturnsAsync(new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>
            {
                [new(itemId, priceTypeId)] = new(itemId, priceTypeId, 42m, "USD", new DateOnly(2026, 4, 18), null)
            });
        pricing
            .Setup(x => x.GetLatestUnitCostsAsync(It.IsAny<IReadOnlyCollection<TradeWarehouseItemKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TradeWarehouseItemKey, decimal>());

        var sut = CreateSut(pricing.Object, timeProvider: timeProvider);

        await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.SalesInvoice,
                AsOfDate: "not-a-date",
                WarehouseId: Guid.NewGuid(),
                PriceTypeId: priceTypeId,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        capturedAsOf.Should().Be(new DateOnly(2026, 4, 18));
    }

    [Fact]
    public async Task ResolveAsync_ItemPriceUpdate_UsesItemProfilePriceType_AndDefaultCurrency_WhenNoCurrentPriceExists()
    {
        var itemId = Guid.NewGuid();
        var defaultPriceTypeId = Guid.NewGuid();

        var pricing = new Mock<ITradePricingLookupReader>();
        pricing
            .Setup(x => x.GetItemSalesProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TradeItemSalesProfile>
            {
                [itemId] = new(itemId, defaultPriceTypeId, "Retail")
            });
        pricing
            .Setup(x => x.GetLatestItemPricesAsync(It.IsAny<IReadOnlyCollection<TradePriceLookupKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>());

        var sut = CreateSut(pricing.Object);

        var response = await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.ItemPriceUpdate,
                AsOfDate: "2026-04-18",
                WarehouseId: null,
                PriceTypeId: null,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        response.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                RowKey = "line-1",
                UnitPrice = (decimal?)null,
                Currency = TradeCodes.DefaultCurrency,
                UnitCost = (decimal?)null
            });
        response.Rows[0].PriceType.Should().BeEquivalentTo(new
        {
            Id = defaultPriceTypeId,
            Display = "Retail"
        });
        pricing.Verify(
            x => x.GetLatestUnitCostsAsync(It.IsAny<IReadOnlyCollection<TradeWarehouseItemKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_CustomerReturn_UsesEarliestSalesInvoiceLinePerItem()
    {
        var salesInvoiceId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var priceTypeId = Guid.NewGuid();
        var pricing = new Mock<ITradePricingLookupReader>();
        pricing
            .Setup(x => x.GetItemSalesProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TradeItemSalesProfile>
            {
                [itemId] = new(itemId, priceTypeId, "Retail")
            });
        pricing
            .Setup(x => x.GetLatestItemPricesAsync(It.IsAny<IReadOnlyCollection<TradePriceLookupKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>
            {
                [new(itemId, priceTypeId)] = new(itemId, priceTypeId, 999m, "USD", new DateOnly(2026, 4, 15), null)
            });
        pricing
            .Setup(x => x.GetLatestUnitCostsAsync(It.IsAny<IReadOnlyCollection<TradeWarehouseItemKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TradeWarehouseItemKey, decimal>
            {
                [new(warehouseId, itemId)] = 777m
            });

        var readers = new Mock<ITradeDocumentReaders>();
        readers
            .Setup(x => x.ReadSalesInvoiceLinesAsync(salesInvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TradeSalesInvoiceLine(salesInvoiceId, 2, itemId, 1m, 22m, 9m, 22m),
                new TradeSalesInvoiceLine(salesInvoiceId, 1, itemId, 1m, 15m, 6m, 15m)
            ]);

        var sut = CreateSut(pricing.Object, readers.Object);

        var response = await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.CustomerReturn,
                AsOfDate: "2026-04-18",
                WarehouseId: warehouseId,
                PriceTypeId: null,
                SalesInvoiceId: salesInvoiceId,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        response.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TradeDocumentLineDefaultsRowResultDto(
                RowKey: "line-1",
                PriceType: null,
                UnitPrice: 15m,
                Currency: null,
                UnitCost: 6m));
    }

    [Fact]
    public async Task ResolveAsync_VendorReturn_UsesEarliestPurchaseReceiptLinePerItem()
    {
        var purchaseReceiptId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var pricing = new Mock<ITradePricingLookupReader>();
        pricing
            .Setup(x => x.GetItemSalesProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TradeItemSalesProfile>());
        pricing
            .Setup(x => x.GetLatestUnitCostsAsync(It.IsAny<IReadOnlyCollection<TradeWarehouseItemKey>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TradeWarehouseItemKey, decimal>
            {
                [new(warehouseId, itemId)] = 41m
            });

        var readers = new Mock<ITradeDocumentReaders>();
        readers
            .Setup(x => x.ReadPurchaseReceiptLinesAsync(purchaseReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TradePurchaseReceiptLine(purchaseReceiptId, 4, itemId, 1m, 23m, 23m),
                new TradePurchaseReceiptLine(purchaseReceiptId, 1, itemId, 1m, 19.5m, 19.5m)
            ]);

        var sut = CreateSut(pricing.Object, readers.Object);

        var response = await sut.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.VendorReturn,
                AsOfDate: "2026-04-18",
                WarehouseId: warehouseId,
                PriceTypeId: null,
                SalesInvoiceId: null,
                PurchaseReceiptId: purchaseReceiptId,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        response.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TradeDocumentLineDefaultsRowResultDto(
                RowKey: "line-1",
                PriceType: null,
                UnitPrice: null,
                Currency: null,
                UnitCost: 19.5m));
    }

    private static TradeDocumentLineDefaultsService CreateSut(
        ITradePricingLookupReader? pricing = null,
        ITradeDocumentReaders? readers = null,
        IUnitOfWork? uow = null,
        TimeProvider? timeProvider = null)
        => new(
            pricing ?? Mock.Of<ITradePricingLookupReader>(),
            readers ?? Mock.Of<ITradeDocumentReaders>(),
            uow ?? CreateUnitOfWorkMock().Object,
            timeProvider ?? new TestTimeProvider(new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero)));

    private static Mock<IUnitOfWork> CreateUnitOfWorkMock()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return uow;
    }
}
