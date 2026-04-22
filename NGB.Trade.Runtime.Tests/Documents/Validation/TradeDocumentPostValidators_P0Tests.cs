using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Documents.Validation;
using NGB.Trade.Runtime.Policy;
using static NGB.Trade.Runtime.Tests.Documents.Validation.TradePostValidatorTestSupport;

namespace NGB.Trade.Runtime.Tests.Documents.Validation;

public sealed class PurchaseReceiptPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Be("Purchase Receipt must contain at least one line.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenVendorIsNotMarkedAsVendor_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = false }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("vendor_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
            WarehouseEntry(head.WarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("warehouse_id");
        ex.Which.Reason.Should().Be("Referenced warehouse is inactive.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLineItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
        ex.Which.Reason.Should().Be("Selected item must be marked as an inventory item.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenQuantityIsNonPositive_Throws(decimal quantity)
    {
        var head = ValidHead();
        var line = ValidLine() with { Quantity = quantity };
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity");
        ex.Which.Reason.Should().Be("Quantity must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitCostIsNonPositive_Throws(decimal unitCost)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitCost = unitCost };
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_cost");
        ex.Which.Reason.Should().Be("Unit Cost must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenLineAmountIsNonPositive_Throws(decimal lineAmount)
    {
        var head = ValidHead();
        var line = ValidLine() with { LineAmount = lineAmount };
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be("Line Amount must be greater than zero.");
    }

    [Theory]
    [InlineData(3, 2.5, 7.4, "7.5")]
    [InlineData(3, 2.5, 7.6, "7.5")]
    [InlineData(2.3456, 1.1111, 2.605, "2.6062")]
    public async Task ValidateBeforePostAsync_WhenLineAmountDoesNotMatchCostMath_Throws(
        decimal quantity,
        decimal unitCost,
        decimal lineAmount,
        string expectedAmountDisplay)
    {
        var head = ValidHead();
        var line = ValidLine() with
        {
            Quantity = quantity,
            UnitCost = unitCost,
            LineAmount = lineAmount
        };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be($"Line Amount must equal Quantity x Unit Cost ({expectedAmountDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenHeadAndLinesAreConsistent_Passes()
    {
        var head = ValidHead();
        var firstLine = ValidLine();
        var secondLine = firstLine with { Ordinal = 2, ItemId = Guid.NewGuid(), Quantity = 1m, UnitCost = 2m, LineAmount = 2m };
        var sut = CreateSut(
            CreateReaders(head, [firstLine, secondLine]).Object,
            CreateCatalogsFor(head, firstLine, secondLine).Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static PurchaseReceiptPostValidator CreateSut(ITradeDocumentReaders readers, ICatalogService catalogs)
        => new(readers, catalogs);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradePurchaseReceiptHead head,
        IReadOnlyList<TradePurchaseReceiptLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPurchaseReceiptHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadPurchaseReceiptLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradePurchaseReceiptHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), "Stock receipt", 25m);

    private static TradePurchaseReceiptLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), 5m, 5m, 25m);

    private static Mock<ICatalogService> CreateCatalogsFor(
        TradePurchaseReceiptHead head,
        params TradePurchaseReceiptLine[] lines)
        => CreateCatalogs(
            [
                PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
                WarehouseEntry(head.WarehouseId),
                .. lines.Select(static line => ItemEntry(line.ItemId))
            ]);

    private static DocumentRecord CreateDocument(string typeCode)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = typeCode,
            Status = DocumentStatus.Draft,
            DateUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
}

public sealed class SalesInvoicePostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.PurchaseReceipt), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Be("Sales Invoice must contain at least one line.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCustomerIsNotMarkedAsCustomer_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = false }),
            WarehouseEntry(head.WarehouseId),
            PriceTypeEntry(head.PriceTypeId!.Value),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("customer_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            PriceTypeEntry(head.PriceTypeId!.Value),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenPriceTypeIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId),
            PriceTypeEntry(head.PriceTypeId!.Value, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("price_type_id");
        ex.Which.Reason.Should().Be("Referenced price type is inactive.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId),
            PriceTypeEntry(head.PriceTypeId!.Value),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenQuantityIsNonPositive_Throws(decimal quantity)
    {
        var head = ValidHead();
        var line = ValidLine() with { Quantity = quantity };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitPriceIsNonPositive_Throws(decimal unitPrice)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitPrice = unitPrice };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_price");
        ex.Which.Reason.Should().Be("Unit Price must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitCostIsNonPositive_Throws(decimal unitCost)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitCost = unitCost };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_cost");
        ex.Which.Reason.Should().Be("Unit Cost Snapshot must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenLineAmountIsNonPositive_Throws(decimal lineAmount)
    {
        var head = ValidHead();
        var line = ValidLine() with { LineAmount = lineAmount };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be("Line Amount must be greater than zero.");
    }

    [Theory]
    [InlineData(3, 2.5, 7.4, "7.5")]
    [InlineData(3, 2.5, 7.6, "7.5")]
    [InlineData(2.3456, 1.1111, 2.605, "2.6062")]
    public async Task ValidateBeforePostAsync_WhenLineAmountDoesNotMatchPriceMath_Throws(
        decimal quantity,
        decimal unitPrice,
        decimal lineAmount,
        string expectedAmountDisplay)
    {
        var head = ValidHead();
        var line = ValidLine() with
        {
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineAmount = lineAmount
        };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be($"Line Amount must equal Quantity x Unit Price ({expectedAmountDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenDocumentIsConsistentAndInventoryExists_Passes()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, line.Quantity));

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static SalesInvoicePostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        TradeInventoryAvailabilityService inventoryAvailability)
        => new(readers, catalogs, inventoryAvailability);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradeSalesInvoiceHead head,
        IReadOnlyList<TradeSalesInvoiceLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadSalesInvoiceHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadSalesInvoiceLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradeSalesInvoiceHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Shipment", 25m);

    private static TradeSalesInvoiceLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), 5m, 5m, 3m, 25m);

    private static Mock<ICatalogService> CreateCatalogsFor(TradeSalesInvoiceHead head, TradeSalesInvoiceLine line)
        => CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId),
            PriceTypeEntry(head.PriceTypeId!.Value),
            ItemEntry(line.ItemId));
}

public sealed class InventoryTransferPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenFromWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.FromWarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            WarehouseEntry(head.ToWarehouseId),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("from_warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenToWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.FromWarehouseId),
            WarehouseEntry(head.ToWarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("to_warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehousesMatch_Throws()
    {
        var warehouseId = Guid.NewGuid();
        var head = ValidHead() with { FromWarehouseId = warehouseId, ToWarehouseId = warehouseId };
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogs(WarehouseEntry(warehouseId), ItemEntry(line.ItemId)).Object,
            CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("to_warehouse_id");
        ex.Which.Reason.Should().Be("From Warehouse and To Warehouse must be different.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.FromWarehouseId),
            WarehouseEntry(head.ToWarehouseId),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenQuantityIsNonPositive_Throws(decimal quantity)
    {
        var head = ValidHead();
        var line = ValidLine() with { Quantity = quantity };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity");
        ex.Which.Reason.Should().Be("Quantity must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenInventoryExists_Passes()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.FromWarehouseId, line.ItemId, line.Quantity));

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static InventoryTransferPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        TradeInventoryAvailabilityService inventoryAvailability)
        => new(readers, catalogs, inventoryAvailability);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradeInventoryTransferHead head,
        IReadOnlyList<TradeInventoryTransferLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadInventoryTransferHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadInventoryTransferLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradeInventoryTransferHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), "Rebalance");

    private static TradeInventoryTransferLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), 5m);

    private static Mock<ICatalogService> CreateCatalogsFor(TradeInventoryTransferHead head, TradeInventoryTransferLine line)
        => CreateCatalogs(WarehouseEntry(head.FromWarehouseId), WarehouseEntry(head.ToWarehouseId), ItemEntry(line.ItemId));
}

public sealed class InventoryAdjustmentPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryTransfer), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object,
            CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.WarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ReasonEntry(head.ReasonId),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenReasonIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.WarehouseId),
            ReasonEntry(head.ReasonId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("reason_id");
        ex.Which.Reason.Should().Be("Referenced inventory adjustment reason is inactive.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            WarehouseEntry(head.WarehouseId),
            ReasonEntry(head.ReasonId),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenQuantityDeltaIsZero_Throws()
    {
        var head = ValidHead();
        var line = ValidLine() with { QuantityDelta = 0m };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity_delta");
        ex.Which.Reason.Should().Be("Quantity Delta must not be zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitCostIsNonPositive_Throws(decimal unitCost)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitCost = unitCost };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_cost");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenLineAmountIsNonPositive_Throws(decimal lineAmount)
    {
        var head = ValidHead();
        var line = ValidLine() with { LineAmount = lineAmount };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
    }

    [Theory]
    [InlineData(-3, 2.5, 7.4, "7.5")]
    [InlineData(-3, 2.5, 7.6, "7.5")]
    [InlineData(2.3456, 1.1111, 2.605, "2.6062")]
    public async Task ValidateBeforePostAsync_WhenLineAmountDoesNotMatchAbsoluteCostMath_Throws(
        decimal quantityDelta,
        decimal unitCost,
        decimal lineAmount,
        string expectedAmountDisplay)
    {
        var head = ValidHead();
        var line = ValidLine() with
        {
            QuantityDelta = quantityDelta,
            UnitCost = unitCost,
            LineAmount = lineAmount
        };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateStrictInventoryAvailabilityService());

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be($"Line Amount must equal abs(Quantity Delta) x Unit Cost ({expectedAmountDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenPositiveAdjustmentIsConsistent_Passes_WithoutTouchingInventoryReaders()
    {
        var head = ValidHead();
        var line = ValidLine() with { QuantityDelta = 2m, UnitCost = 5m, LineAmount = 10m };
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateStrictInventoryAvailabilityService());

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenNegativeAdjustmentHasEnoughInventory_Passes()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, Math.Abs(line.QuantityDelta)));

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static InventoryAdjustmentPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        TradeInventoryAvailabilityService inventoryAvailability)
        => new(readers, catalogs, inventoryAvailability);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradeInventoryAdjustmentHead head,
        IReadOnlyList<TradeInventoryAdjustmentLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadInventoryAdjustmentHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadInventoryAdjustmentLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradeInventoryAdjustmentHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), "Cycle count", 25m);

    private static TradeInventoryAdjustmentLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), -5m, 5m, 25m);

    private static Mock<ICatalogService> CreateCatalogsFor(TradeInventoryAdjustmentHead head, TradeInventoryAdjustmentLine line)
        => CreateCatalogs(WarehouseEntry(head.WarehouseId), ReasonEntry(head.ReasonId), ItemEntry(line.ItemId));
}

public sealed class CustomerReturnPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.SalesInvoice), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCustomerIsNotMarkedAsCustomer_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = false }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("customer_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenSalesInvoiceBelongsToDifferentCustomer_Throws()
    {
        var salesInvoiceId = Guid.NewGuid();
        var head = ValidHead() with { SalesInvoiceId = salesInvoiceId };
        var line = ValidLine();
        var readers = CreateReaders(head, [line]);
        readers
            .Setup(x => x.ReadSalesInvoiceHeadAsync(salesInvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeSalesInvoiceHead(
                salesInvoiceId,
                head.DocumentDateUtc,
                Guid.NewGuid(),
                head.WarehouseId,
                null,
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.SalesInvoice, salesInvoiceId, DocumentStatus.Posted)).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenQuantityIsNonPositive_Throws(decimal quantity)
    {
        var head = ValidHead();
        var line = ValidLine() with { Quantity = quantity };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitPriceIsNonPositive_Throws(decimal unitPrice)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitPrice = unitPrice };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_price");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitCostIsNonPositive_Throws(decimal unitCost)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitCost = unitCost };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_cost");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenLineAmountIsNonPositive_Throws(decimal lineAmount)
    {
        var head = ValidHead();
        var line = ValidLine() with { LineAmount = lineAmount };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
    }

    [Theory]
    [InlineData(3, 2.5, 7.4, "7.5")]
    [InlineData(3, 2.5, 7.6, "7.5")]
    [InlineData(2.3456, 1.1111, 2.605, "2.6062")]
    public async Task ValidateBeforePostAsync_WhenLineAmountDoesNotMatchPriceMath_Throws(
        decimal quantity,
        decimal unitPrice,
        decimal lineAmount,
        string expectedAmountDisplay)
    {
        var head = ValidHead();
        var line = ValidLine() with
        {
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineAmount = lineAmount
        };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be($"Line Amount must equal Quantity x Unit Price ({expectedAmountDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenDocumentIsConsistent_Passes()
    {
        var head = ValidHead() with { SalesInvoiceId = null };
        var line = ValidLine();
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerReturn), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static CustomerReturnPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        IDocumentRepository documents)
        => new(readers, catalogs, documents);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradeCustomerReturnHead head,
        IReadOnlyList<TradeCustomerReturnLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadCustomerReturnHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadCustomerReturnLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradeCustomerReturnHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), null, "Return", 25m);

    private static TradeCustomerReturnLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), 5m, 5m, 3m, 25m);

    private static Mock<ICatalogService> CreateCatalogsFor(TradeCustomerReturnHead head, TradeCustomerReturnLine line)
        => CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId));
}

public sealed class VendorReturnPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, [line]).Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments().Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.InventoryAdjustment), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenLinesAreMissing_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var sut = CreateSut(
            CreateReaders(head, []).Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments().Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenVendorIsNotMarkedAsVendor_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = false }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("vendor_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenWarehouseIsInactive_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
            WarehouseEntry(head.WarehouseId, fields: new Dictionary<string, object?> { ["is_active"] = false }),
            ItemEntry(line.ItemId));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("warehouse_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenPurchaseReceiptBelongsToDifferentVendor_Throws()
    {
        var purchaseReceiptId = Guid.NewGuid();
        var head = ValidHead() with { PurchaseReceiptId = purchaseReceiptId };
        var line = ValidLine();
        var readers = CreateReaders(head, [line]);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(purchaseReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                purchaseReceiptId,
                head.DocumentDateUtc,
                Guid.NewGuid(),
                head.WarehouseId,
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.PurchaseReceipt, purchaseReceiptId, DocumentStatus.Posted)).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenItemIsNotInventory_Throws()
    {
        var head = ValidHead();
        var line = ValidLine();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_inventory_item"] = false }));
        var sut = CreateSut(CreateReaders(head, [line]).Object, catalogs.Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenQuantityIsNonPositive_Throws(decimal quantity)
    {
        var head = ValidHead();
        var line = ValidLine() with { Quantity = quantity };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].quantity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenUnitCostIsNonPositive_Throws(decimal unitCost)
    {
        var head = ValidHead();
        var line = ValidLine() with { UnitCost = unitCost };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].unit_cost");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenLineAmountIsNonPositive_Throws(decimal lineAmount)
    {
        var head = ValidHead();
        var line = ValidLine() with { LineAmount = lineAmount };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
    }

    [Theory]
    [InlineData(3, 2.5, 7.4, "7.5")]
    [InlineData(3, 2.5, 7.6, "7.5")]
    [InlineData(2.3456, 1.1111, 2.605, "2.6062")]
    public async Task ValidateBeforePostAsync_WhenLineAmountDoesNotMatchCostMath_Throws(
        decimal quantity,
        decimal unitCost,
        decimal lineAmount,
        string expectedAmountDisplay)
    {
        var head = ValidHead();
        var line = ValidLine() with
        {
            Quantity = quantity,
            UnitCost = unitCost,
            LineAmount = lineAmount
        };
        var sut = CreateSut(CreateReaders(head, [line]).Object, CreateCatalogsFor(head, line).Object, CreateDocuments().Object, CreateAvailableInventoryService(head.WarehouseId, line.ItemId, 100m));

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].line_amount");
        ex.Which.Reason.Should().Be($"Line Amount must equal Quantity x Unit Cost ({expectedAmountDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenDocumentIsConsistentAndInventoryExists_Passes()
    {
        var purchaseReceiptId = Guid.NewGuid();
        var head = ValidHead() with { PurchaseReceiptId = purchaseReceiptId };
        var line = ValidLine();
        var readers = CreateReaders(head, [line]);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(purchaseReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                purchaseReceiptId,
                head.DocumentDateUtc,
                head.VendorId,
                head.WarehouseId,
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head, line).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.PurchaseReceipt, purchaseReceiptId, DocumentStatus.Posted)).Object,
            CreateAvailableInventoryService(head.WarehouseId, line.ItemId, line.Quantity));

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorReturn), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static VendorReturnPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        IDocumentRepository documents,
        TradeInventoryAvailabilityService inventoryAvailability)
        => new(readers, catalogs, documents, inventoryAvailability);

    private static Mock<ITradeDocumentReaders> CreateReaders(
        TradeVendorReturnHead head,
        IReadOnlyList<TradeVendorReturnLine> lines)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadVendorReturnHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        readers.Setup(x => x.ReadVendorReturnLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(lines);
        return readers;
    }

    private static TradeVendorReturnHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), Guid.NewGuid(), null, "Vendor return", 25m);

    private static TradeVendorReturnLine ValidLine()
        => new(Guid.NewGuid(), 1, Guid.NewGuid(), 5m, 5m, 25m);

    private static Mock<ICatalogService> CreateCatalogsFor(TradeVendorReturnHead head, TradeVendorReturnLine line)
        => CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }),
            WarehouseEntry(head.WarehouseId),
            ItemEntry(line.ItemId));
}

public sealed class CustomerPaymentPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCustomerIsNotMarkedAsCustomer_Throws()
    {
        var head = ValidHead();
        var catalogs = CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = false }));
        var sut = CreateSut(CreateReaders(head).Object, catalogs.Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("customer_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenAmountIsNonPositive_Throws(decimal amount)
    {
        var head = ValidHead() with { Amount = amount };
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("amount");
        ex.Which.Reason.Should().Be("Amount must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCashAccountIsNotAnAsset_Throws()
    {
        var cashAccountId = Guid.NewGuid();
        var head = ValidHead() with { CashAccountId = cashAccountId };
        var sut = CreateSut(
            CreateReaders(head).Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments().Object,
            CreateChartProvider(CreateAccount(cashAccountId, AccountType.Liability, StatementSection.Liabilities)).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("cash_account_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenSalesInvoiceBelongsToDifferentCustomer_Throws()
    {
        var salesInvoiceId = Guid.NewGuid();
        var head = ValidHead() with { SalesInvoiceId = salesInvoiceId };
        var readers = CreateReaders(head);
        readers
            .Setup(x => x.ReadSalesInvoiceHeadAsync(salesInvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeSalesInvoiceHead(
                salesInvoiceId,
                head.DocumentDateUtc,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.SalesInvoice, salesInvoiceId, DocumentStatus.Posted)).Object,
            CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenOptionalReferencesAreMissing_Passes()
    {
        var head = ValidHead() with { CashAccountId = null, SalesInvoiceId = null };
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCashAccountAndSalesInvoiceAreValid_Passes()
    {
        var cashAccountId = Guid.NewGuid();
        var salesInvoiceId = Guid.NewGuid();
        var head = ValidHead() with { CashAccountId = cashAccountId, SalesInvoiceId = salesInvoiceId };
        var readers = CreateReaders(head);
        readers
            .Setup(x => x.ReadSalesInvoiceHeadAsync(salesInvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeSalesInvoiceHead(
                salesInvoiceId,
                head.DocumentDateUtc,
                head.CustomerId,
                Guid.NewGuid(),
                null,
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.SalesInvoice, salesInvoiceId, DocumentStatus.Posted)).Object,
            CreateChartProvider(CreateAccount(cashAccountId, AccountType.Asset, StatementSection.Assets)).Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static CustomerPaymentPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        IDocumentRepository documents,
        IChartOfAccountsProvider charts)
        => new(readers, catalogs, documents, charts);

    private static Mock<ITradeDocumentReaders> CreateReaders(TradeCustomerPaymentHead head)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadCustomerPaymentHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        return readers;
    }

    private static TradeCustomerPaymentHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), null, null, 25m, "Payment");

    private static Mock<ICatalogService> CreateCatalogsFor(TradeCustomerPaymentHead head)
        => CreateCatalogs(
            PartyEntry(head.CustomerId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_customer"] = true }));
}

public sealed class VendorPaymentPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_WhenBoundToDifferentType_Throws()
    {
        var head = ValidHead();
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.CustomerPayment), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenVendorIsNotMarkedAsVendor_Throws()
    {
        var head = ValidHead();
        var catalogs = CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = false }));
        var sut = CreateSut(CreateReaders(head).Object, catalogs.Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("vendor_id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_WhenAmountIsNonPositive_Throws(decimal amount)
    {
        var head = ValidHead() with { Amount = amount };
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("amount");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCashAccountIsNotAnAsset_Throws()
    {
        var cashAccountId = Guid.NewGuid();
        var head = ValidHead() with { CashAccountId = cashAccountId };
        var sut = CreateSut(
            CreateReaders(head).Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments().Object,
            CreateChartProvider(CreateAccount(cashAccountId, AccountType.Equity, StatementSection.Equity)).Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("cash_account_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenPurchaseReceiptBelongsToDifferentVendor_Throws()
    {
        var purchaseReceiptId = Guid.NewGuid();
        var head = ValidHead() with { PurchaseReceiptId = purchaseReceiptId };
        var readers = CreateReaders(head);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(purchaseReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                purchaseReceiptId,
                head.DocumentDateUtc,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.PurchaseReceipt, purchaseReceiptId, DocumentStatus.Posted)).Object,
            CreateChartProvider().Object);

        Func<Task> act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenOptionalReferencesAreMissing_Passes()
    {
        var head = ValidHead() with { CashAccountId = null, PurchaseReceiptId = null };
        var sut = CreateSut(CreateReaders(head).Object, CreateCatalogsFor(head).Object, CreateDocuments().Object, CreateChartProvider().Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_WhenCashAccountAndPurchaseReceiptAreValid_Passes()
    {
        var cashAccountId = Guid.NewGuid();
        var purchaseReceiptId = Guid.NewGuid();
        var head = ValidHead() with { CashAccountId = cashAccountId, PurchaseReceiptId = purchaseReceiptId };
        var readers = CreateReaders(head);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(purchaseReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                purchaseReceiptId,
                head.DocumentDateUtc,
                head.VendorId,
                Guid.NewGuid(),
                null,
                10m));
        var sut = CreateSut(
            readers.Object,
            CreateCatalogsFor(head).Object,
            CreateDocuments(CreateDocumentRecord(TradeCodes.PurchaseReceipt, purchaseReceiptId, DocumentStatus.Posted)).Object,
            CreateChartProvider(CreateAccount(cashAccountId, AccountType.Asset, StatementSection.Assets)).Object);

        var act = () => sut.ValidateBeforePostAsync(CreateDocument(TradeCodes.VendorPayment), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static VendorPaymentPostValidator CreateSut(
        ITradeDocumentReaders readers,
        ICatalogService catalogs,
        IDocumentRepository documents,
        IChartOfAccountsProvider charts)
        => new(readers, catalogs, documents, charts);

    private static Mock<ITradeDocumentReaders> CreateReaders(TradeVendorPaymentHead head)
    {
        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadVendorPaymentHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(head);
        return readers;
    }

    private static TradeVendorPaymentHead ValidHead()
        => new(Guid.NewGuid(), new DateOnly(2026, 4, 18), Guid.NewGuid(), null, null, 25m, "Payment");

    private static Mock<ICatalogService> CreateCatalogsFor(TradeVendorPaymentHead head)
        => CreateCatalogs(
            PartyEntry(head.VendorId, fields: new Dictionary<string, object?> { ["is_active"] = true, ["is_vendor"] = true }));
}

internal static class TradePostValidatorTestSupport
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    internal readonly record struct CatalogEntry(string CatalogType, Guid Id, CatalogItemDto Item);

    internal static Mock<ICatalogService> CreateCatalogs(params CatalogEntry[] entries)
    {
        var byKey = entries.ToDictionary(static x => (x.CatalogType, x.Id), static x => x.Item);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs
            .Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string catalogType, Guid id, CancellationToken _) => byKey[(catalogType, id)]);
        return catalogs;
    }

    internal static CatalogEntry PartyEntry(Guid id, IReadOnlyDictionary<string, object?>? fields = null)
        => new(TradeCodes.Party, id, CreateCatalogItem(id, fields));

    internal static CatalogEntry WarehouseEntry(Guid id, IReadOnlyDictionary<string, object?>? fields = null)
        => new(TradeCodes.Warehouse, id, CreateCatalogItem(id, fields ?? new Dictionary<string, object?> { ["is_active"] = true }));

    internal static CatalogEntry ItemEntry(Guid id, IReadOnlyDictionary<string, object?>? fields = null)
        => new(
            TradeCodes.Item,
            id,
            CreateCatalogItem(id, fields ?? new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_inventory_item"] = true
            }));

    internal static CatalogEntry PriceTypeEntry(Guid id, IReadOnlyDictionary<string, object?>? fields = null)
        => new(TradeCodes.PriceType, id, CreateCatalogItem(id, fields ?? new Dictionary<string, object?> { ["is_active"] = true }));

    internal static CatalogEntry ReasonEntry(Guid id, IReadOnlyDictionary<string, object?>? fields = null)
        => new(TradeCodes.InventoryAdjustmentReason, id, CreateCatalogItem(id, fields ?? new Dictionary<string, object?> { ["is_active"] = true }));

    internal static DocumentRecord CreateDocument(string typeCode)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = typeCode,
            Status = DocumentStatus.Draft,
            DateUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    internal static TradeInventoryAvailabilityService CreateStrictInventoryAvailabilityService()
        => new(
            Mock.Of<ITradeAccountingPolicyReader>(MockBehavior.Strict),
            Mock.Of<IOperationalRegisterReadService>(MockBehavior.Strict),
            Mock.Of<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict),
            Mock.Of<ICatalogService>(MockBehavior.Strict));

    internal static TradeInventoryAvailabilityService CreateAvailableInventoryService(Guid warehouseId, Guid itemId, decimal availableQuantity)
    {
        var registerId = Guid.NewGuid();

        var policyReader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        policyReader
            .Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeAccountingPolicy(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                registerId,
                Guid.NewGuid()));

        var readService = new Mock<IOperationalRegisterReadService>(MockBehavior.Strict);
        readService
            .Setup(x => x.GetBalancesPageAsync(It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
                new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId,
                    request.FromInclusive,
                    request.ToInclusive,
                    [CreateBalanceRow(warehouseId, itemId, availableQuantity)],
                    false,
                    null));

        var movementsReader = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        movementsReader
            .Setup(x => x.GetByMonthsAsync(
                registerId,
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null,
                null,
                null,
                null,
                1000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OperationalRegisterMovementQueryReadRow>());

        return new TradeInventoryAvailabilityService(
            policyReader.Object,
            readService.Object,
            movementsReader.Object,
            Mock.Of<ICatalogService>(MockBehavior.Strict));
    }

    internal static Mock<IDocumentRepository> CreateDocuments(params DocumentRecord[] documents)
    {
        var byId = documents.ToDictionary(static x => x.Id);
        var repository = new Mock<IDocumentRepository>(MockBehavior.Strict);
        repository
            .Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => byId.GetValueOrDefault(id));
        return repository;
    }

    internal static DocumentRecord CreateDocumentRecord(string typeCode, Guid id, DocumentStatus status)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            Status = status,
            DateUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    internal static Mock<IChartOfAccountsProvider> CreateChartProvider(params Account[] accounts)
    {
        var chart = new ChartOfAccounts();
        foreach (var account in accounts)
            chart.Add(account);

        var provider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        provider
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart);
        return provider;
    }

    internal static Account CreateAccount(Guid id, AccountType type, StatementSection section)
        => new(
            id,
            code: $"10{id.ToString("N")[..4]}",
            name: "Cash account",
            type,
            statementSection: section);

    private static CatalogItemDto CreateCatalogItem(Guid id, IReadOnlyDictionary<string, object?>? fields)
    {
        IReadOnlyDictionary<string, JsonElement>? payloadFields = null;
        if (fields is not null)
        {
            payloadFields = fields.ToDictionary(
                static pair => pair.Key,
                static pair => JsonSerializer.SerializeToElement(pair.Value));
        }

        return new CatalogItemDto(id, "Catalog Item", new RecordPayload(payloadFields, null), false, false);
    }

    private static OperationalRegisterMonthlyProjectionReadRow CreateBalanceRow(Guid warehouseId, Guid itemId, decimal quantity)
        => new()
        {
            PeriodMonth = new DateOnly(2026, 4, 1),
            DimensionSetId = Guid.NewGuid(),
            Dimensions = new DimensionBag(
            [
                new DimensionValue(ItemDimensionId, itemId),
                new DimensionValue(WarehouseDimensionId, warehouseId)
            ]),
            DimensionValueDisplays = new Dictionary<Guid, string>(),
            Values = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["qty_delta"] = quantity
            }
        };
}
