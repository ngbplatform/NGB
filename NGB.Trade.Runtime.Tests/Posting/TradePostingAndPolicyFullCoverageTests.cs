using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Runtime.Posting;

namespace NGB.Trade.Runtime.Tests.Posting;

public sealed class TradePostingAndPolicyFullCoverageTests
{
    [Fact]
    public async Task AccountingPolicyReader_MapsACompletePolicy()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        var item = Catalog(new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["cash_account_id"] = JsonSerializer.SerializeToElement(ids[0]),
            ["ar_account_id"] = JsonSerializer.SerializeToElement(ids[1]),
            ["inventory_account_id"] = JsonSerializer.SerializeToElement(ids[2]),
            ["ap_account_id"] = JsonSerializer.SerializeToElement(ids[3]),
            ["sales_revenue_account_id"] = JsonSerializer.SerializeToElement(ids[4]),
            ["cogs_account_id"] = JsonSerializer.SerializeToElement(ids[5]),
            ["inventory_adjustment_account_id"] = JsonSerializer.SerializeToElement(ids[6]),
            ["inventory_movements_register_id"] = JsonSerializer.SerializeToElement(ids[7]),
            ["item_prices_register_id"] = JsonSerializer.SerializeToElement(ids[8])
        }));

        var result = await new TradeAccountingPolicyReader(CatalogPage([item])).GetRequiredAsync();

        result.CashAccountId.Should().Be(ids[0]);
        result.ItemPricesRegisterId.Should().Be(ids[8]);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("multiple")]
    [InlineData("null-fields")]
    [InlineData("missing-field")]
    [InlineData("invalid-guid")]
    public async Task AccountingPolicyReader_RejectsEveryInvalidConfiguration(string scenario)
    {
        IReadOnlyList<CatalogItemDto> items = scenario switch
        {
            "empty" => [],
            "multiple" => [Catalog(new RecordPayload()), Catalog(new RecordPayload())],
            "null-fields" => [Catalog(new RecordPayload())],
            "missing-field" => [Catalog(new RecordPayload(new Dictionary<string, JsonElement>()))],
            _ => [Catalog(new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["cash_account_id"] = JsonSerializer.SerializeToElement("not-a-guid")
            }))]
        };
        var act = () => new TradeAccountingPolicyReader(CatalogPage(items)).GetRequiredAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task AccountingPostingHandlers_EmitAllExpectedEntriesAndCashOverridePaths()
    {
        var data = new TradeReaderStub();
        var policy = Policy();
        var overrideCash = Guid.CreateVersion7();
        var chart = Chart(policy, overrideCash);
        var posts = new List<PostCall>();
        var context = PostingContext(chart, posts);
        var document = Document();
        var handlers = new (string Code, NGB.Definitions.Documents.Posting.IDocumentPostingHandler Handler)[]
        {
            (TradeCodes.PurchaseReceipt, new PurchaseReceiptPostingHandler(data, PolicyReader(policy))),
            (TradeCodes.SalesInvoice, new SalesInvoicePostingHandler(data, PolicyReader(policy))),
            (TradeCodes.CustomerPayment, new CustomerPaymentPostingHandler(data, PolicyReader(policy))),
            (TradeCodes.VendorPayment, new VendorPaymentPostingHandler(data, PolicyReader(policy))),
            (TradeCodes.InventoryAdjustment, new InventoryAdjustmentPostingHandler(data, PolicyReader(policy))),
            (TradeCodes.CustomerReturn, new CustomerReturnPostingHandler(data, PolicyReader(policy))),
            (TradeCodes.VendorReturn, new VendorReturnPostingHandler(data, PolicyReader(policy)))
        };

        foreach (var (code, handler) in handlers)
        {
            handler.TypeCode.Should().Be(code);
            await handler.BuildEntriesAsync(document, context.Object, CancellationToken.None);
        }

        data.CustomerPayment = data.CustomerPayment with { CashAccountId = overrideCash };
        data.VendorPayment = data.VendorPayment with { CashAccountId = overrideCash };
        await new CustomerPaymentPostingHandler(data, PolicyReader(policy))
            .BuildEntriesAsync(document, context.Object, CancellationToken.None);
        await new VendorPaymentPostingHandler(data, PolicyReader(policy))
            .BuildEntriesAsync(document, context.Object, CancellationToken.None);

        posts.Should().HaveCount(12);
        posts.Should().Contain(post => post.Debit.Id == overrideCash);
        posts.Should().Contain(post => post.Credit.Id == overrideCash);
        posts.Should().Contain(post => post.Amount == 3.7037m);
    }

    [Fact]
    public async Task OperationalPostingHandlers_RejectMissingRegisterThenEmitEveryMovementDirection()
    {
        var data = new TradeReaderStub();
        var policy = Policy();
        var document = Document();
        var dimensions = DimensionSets();
        var missing = RegisterRepository(null);
        var missingHandlers = OperationalHandlers(data, policy, missing.Object, dimensions.Object);

        foreach (var (code, handler) in missingHandlers)
        {
            handler.TypeCode.Should().Be(code);
            var act = () => handler.BuildMovementsAsync(
                document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        }

        var register = Register(policy.InventoryMovementsRegisterId);
        var movements = new List<OperationalRegisterMovement>();
        var builder = MovementBuilder(movements);
        foreach (var (code, handler) in OperationalHandlers(
                     data, policy, RegisterRepository(register).Object, dimensions.Object))
        {
            handler.TypeCode.Should().Be(code);
            await handler.BuildMovementsAsync(document, builder.Object, CancellationToken.None);
        }

        movements.Should().HaveCount(8);
        movements.Select(movement => movement.Resources["qty_delta"])
            .Should().Contain(value => value > 0m).And.Contain(value => value < 0m);
    }

    [Fact]
    public async Task ItemPriceReferencePosting_CoversCurrencyFallbackNormalizationAndDeleteFlag()
    {
        var data = new TradeReaderStub
        {
            PriceLines =
            [
                new TradeItemPriceUpdateLine(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(), " ", 10m),
                new TradeItemPriceUpdateLine(Guid.CreateVersion7(), 2, Guid.CreateVersion7(), Guid.CreateVersion7(), " eur ", 20m)
            ]
        };
        var records = new List<ReferenceRegisterRecordWrite>();
        var builder = ReferenceBuilder(records);
        var handler = new ItemPriceUpdateReferenceRegisterPostingHandler(data, DimensionSets().Object);

        handler.TypeCode.Should().Be(TradeCodes.ItemPriceUpdate);
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Post, builder.Object, CancellationToken.None);
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Unpost, builder.Object, CancellationToken.None);

        records.Should().HaveCount(4);
        records[0].Values["currency"].Should().Be(TradeCodes.DefaultCurrency);
        records[1].Values["currency"].Should().Be("EUR");
        records.Select(record => record.IsDeleted).Should().Equal(false, false, true, true);
    }

    private static IReadOnlyList<(string Code, NGB.Definitions.Documents.Posting.IDocumentOperationalRegisterPostingHandler Handler)>
        OperationalHandlers(
            ITradeDocumentReaders data,
            TradeAccountingPolicy policy,
            IOperationalRegisterRepository registers,
            IDimensionSetService dimensions) =>
        [
            (TradeCodes.PurchaseReceipt, new PurchaseReceiptInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions)),
            (TradeCodes.SalesInvoice, new SalesInvoiceInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions)),
            (TradeCodes.InventoryTransfer, new InventoryTransferInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions)),
            (TradeCodes.InventoryAdjustment, new InventoryAdjustmentInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions)),
            (TradeCodes.CustomerReturn, new CustomerReturnInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions)),
            (TradeCodes.VendorReturn, new VendorReturnInventoryOperationalRegisterPostingHandler(data, PolicyReader(policy), registers, dimensions))
        ];

    private static ICatalogService CatalogPage(IReadOnlyList<CatalogItemDto> items)
    {
        var service = new Mock<ICatalogService>(MockBehavior.Strict);
        service.Setup(x => x.GetPageAsync(TradeCodes.AccountingPolicy, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(items, 0, 2, items.Count));
        return service.Object;
    }

    private static CatalogItemDto Catalog(RecordPayload payload) =>
        new(Guid.CreateVersion7(), "Policy", payload, false, false);

    private static TradeAccountingPolicy Policy() =>
        new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());

    private static ITradeAccountingPolicyReader PolicyReader(TradeAccountingPolicy policy)
    {
        var reader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        return reader.Object;
    }

    private static ChartOfAccounts Chart(TradeAccountingPolicy policy, Guid overrideCash)
    {
        var chart = new ChartOfAccounts();
        foreach (var id in new[]
                 {
                     policy.CashAccountId,
                     policy.AccountsReceivableAccountId,
                     policy.InventoryAccountId,
                     policy.AccountsPayableAccountId,
                     policy.SalesRevenueAccountId,
                     policy.CostOfGoodsSoldAccountId,
                     policy.InventoryAdjustmentAccountId,
                     overrideCash
                 })
            chart.Add(new Account(id, id.ToString("N"), "Account", AccountType.Asset, StatementSection.Assets));
        return chart;
    }

    private static Mock<IAccountingPostingContext> PostingContext(ChartOfAccounts chart, ICollection<PostCall> posts)
    {
        var context = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        context.Setup(x => x.GetChartOfAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);
        context.Setup(x => x.Post(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Account>(), It.IsAny<Account>(),
                It.IsAny<decimal>(), It.IsAny<DimensionBag>(), It.IsAny<DimensionBag>(), It.IsAny<bool>()))
            .Callback<Guid, DateTime, Account, Account, decimal, DimensionBag?, DimensionBag?, bool>(
                (_, _, debit, credit, amount, _, _, _) => posts.Add(new(debit, credit, amount)));
        return context;
    }

    private static Mock<IOperationalRegisterRepository> RegisterRepository(OperationalRegisterAdminItem? register)
    {
        var repository = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(register);
        return repository;
    }

    private static OperationalRegisterAdminItem Register(Guid id) =>
        new(id, "trade.inventory", "trade.inventory", "trade_inventory", "Inventory", false,
            DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static Mock<IDimensionSetService> DimensionSets()
    {
        var service = new Mock<IDimensionSetService>(MockBehavior.Strict);
        service.Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DimensionBag> bags, CancellationToken _) =>
                bags.Select(static _ => Guid.CreateVersion7()).ToArray());
        return service;
    }

    private static Mock<IOperationalRegisterMovementsBuilder> MovementBuilder(
        ICollection<OperationalRegisterMovement> movements)
    {
        var builder = new Mock<IOperationalRegisterMovementsBuilder>(MockBehavior.Strict);
        builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<OperationalRegisterMovement>()))
            .Callback<string, OperationalRegisterMovement>((_, movement) => movements.Add(movement));
        return builder;
    }

    private static Mock<IReferenceRegisterRecordsBuilder> ReferenceBuilder(
        ICollection<ReferenceRegisterRecordWrite> records)
    {
        var builder = new Mock<IReferenceRegisterRecordsBuilder>(MockBehavior.Strict);
        builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<ReferenceRegisterRecordWrite>()))
            .Callback<string, ReferenceRegisterRecordWrite>((_, record) => records.Add(record));
        return builder;
    }

    private static DocumentRecord Document() => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = "trade.test",
        DateUtc = DateTime.UtcNow,
        Status = DocumentStatus.Posted
    };

    private sealed record PostCall(Account Debit, Account Credit, decimal Amount);

    private sealed class TradeReaderStub : ITradeDocumentReaders
    {
        private readonly Guid _documentId = Guid.CreateVersion7();
        private readonly Guid _partyId = Guid.CreateVersion7();
        private readonly Guid _warehouseId = Guid.CreateVersion7();
        private readonly Guid _itemId = Guid.CreateVersion7();
        private static readonly DateOnly Date = new(2026, 8, 15);

        public TradeCustomerPaymentHead CustomerPayment { get; set; }
        public TradeVendorPaymentHead VendorPayment { get; set; }
        public IReadOnlyList<TradeItemPriceUpdateLine> PriceLines { get; init; }

        public TradeReaderStub()
        {
            CustomerPayment = new(_documentId, Date, _partyId, null, null, 10m, null);
            VendorPayment = new(_documentId, Date, _partyId, null, null, 10m, null);
            PriceLines = [new(_documentId, 1, _itemId, Guid.CreateVersion7(), "USD", 10m)];
        }

        public Task<TradePurchaseReceiptHead> ReadPurchaseReceiptHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradePurchaseReceiptHead(_documentId, Date, _partyId, _warehouseId, null, 10m));
        public Task<IReadOnlyList<TradePurchaseReceiptLine>> ReadPurchaseReceiptLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradePurchaseReceiptLine>>([new(_documentId, 1, _itemId, 2m, 1.23456m, 2.4691m)]);
        public Task<TradeSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeSalesInvoiceHead(_documentId, Date, _partyId, _warehouseId, null, null, 10m));
        public Task<IReadOnlyList<TradeSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeSalesInvoiceLine>>([new(_documentId, 1, _itemId, 3m, 5m, 1.23456m, 15m)]);
        public Task<TradeInventoryTransferHead> ReadInventoryTransferHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeInventoryTransferHead(_documentId, Date, _warehouseId, Guid.CreateVersion7(), null));
        public Task<IReadOnlyList<TradeInventoryTransferLine>> ReadInventoryTransferLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeInventoryTransferLine>>([new(_documentId, 1, _itemId, 2m)]);
        public Task<TradeInventoryAdjustmentHead> ReadInventoryAdjustmentHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeInventoryAdjustmentHead(_documentId, Date, _warehouseId, Guid.CreateVersion7(), null, 10m));
        public Task<IReadOnlyList<TradeInventoryAdjustmentLine>> ReadInventoryAdjustmentLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeInventoryAdjustmentLine>>
            ([new(_documentId, 1, _itemId, 2m, 1m, 2m), new(_documentId, 2, Guid.CreateVersion7(), -2m, 1m, 2m)]);
        public Task<TradeCustomerReturnHead> ReadCustomerReturnHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeCustomerReturnHead(_documentId, Date, _partyId, _warehouseId, null, null, 10m));
        public Task<IReadOnlyList<TradeCustomerReturnLine>> ReadCustomerReturnLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeCustomerReturnLine>>([new(_documentId, 1, _itemId, 2m, 5m, 1m, 10m)]);
        public Task<TradeVendorReturnHead> ReadVendorReturnHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeVendorReturnHead(_documentId, Date, _partyId, _warehouseId, null, null, 10m));
        public Task<IReadOnlyList<TradeVendorReturnLine>> ReadVendorReturnLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeVendorReturnLine>>([new(_documentId, 1, _itemId, 2m, 1m, 2m)]);
        public Task<TradeCustomerPaymentHead> ReadCustomerPaymentHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(CustomerPayment);
        public Task<TradeVendorPaymentHead> ReadVendorPaymentHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(VendorPayment);
        public Task<TradeItemPriceUpdateHead> ReadItemPriceUpdateHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(new TradeItemPriceUpdateHead(_documentId, Date, null));
        public Task<IReadOnlyList<TradeItemPriceUpdateLine>> ReadItemPriceUpdateLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(PriceLines);
    }
}
