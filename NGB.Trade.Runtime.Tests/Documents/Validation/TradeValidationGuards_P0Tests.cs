using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Documents.Validation;

namespace NGB.Trade.Runtime.Tests.Documents.Validation;

public sealed class DocumentValidatorBindingGuard_P0Tests
{
    [Theory]
    [InlineData(TradeCodes.SalesInvoice, TradeCodes.SalesInvoice)]
    [InlineData(TradeCodes.SalesInvoice, "TRD.SALES_INVOICE")]
    [InlineData(TradeCodes.PurchaseReceipt, "TrD.PuRcHaSe_ReCeIpT")]
    public void EnsureExpectedType_AllowsCaseInsensitiveMatches(string expectedTypeCode, string actualTypeCode)
    {
        var document = CreateDocument(actualTypeCode);

        var act = () => DocumentValidatorBindingGuard.EnsureExpectedType(document, expectedTypeCode, "Validator");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(TradeCodes.SalesInvoice, TradeCodes.PurchaseReceipt)]
    [InlineData(TradeCodes.InventoryTransfer, TradeCodes.InventoryAdjustment)]
    [InlineData(TradeCodes.CustomerPayment, TradeCodes.VendorPayment)]
    public void EnsureExpectedType_WhenTypeDiffers_ThrowsConfigurationViolation(
        string expectedTypeCode,
        string actualTypeCode)
    {
        var document = CreateDocument(actualTypeCode);

        var act = () => DocumentValidatorBindingGuard.EnsureExpectedType(document, expectedTypeCode, "Validator");

        var ex = act.Should().Throw<NgbConfigurationViolationException>();
        ex.Which.Message.Should().Contain($"'{expectedTypeCode}'");
        ex.Which.Message.Should().Contain($"'{actualTypeCode}'");
        ex.Which.Context.Should().ContainKey("documentId").WhoseValue.Should().Be(document.Id);
        ex.Which.Context.Should().Contain("expectedTypeCode", expectedTypeCode);
        ex.Which.Context.Should().Contain("actualTypeCode", actualTypeCode);
    }

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

public sealed class TradeAccountingValidationGuards_P0Tests
{
    [Theory]
    [MemberData(nameof(InvalidCashAccountCases))]
    public async Task EnsureCashAccountAsync_Rejects_Invalid_Accounts(
        Guid cashAccountId,
        ChartOfAccounts chart,
        string expectedReason)
    {
        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart);

        Func<Task> act = () => TradeAccountingValidationGuards.EnsureCashAccountAsync(
            cashAccountId,
            "cash_account_id",
            charts.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("cash_account_id");
        ex.Which.Reason.Should().Be(expectedReason);
    }

    [Theory]
    [MemberData(nameof(ValidCashAccountCases))]
    public async Task EnsureCashAccountAsync_Allows_Asset_Accounts_Without_Required_Dimensions(Account account)
    {
        var chart = new ChartOfAccounts();
        chart.Add(account);

        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart);

        var act = () => TradeAccountingValidationGuards.EnsureCashAccountAsync(
            account.Id,
            "cash_account_id",
            charts.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public static IEnumerable<object[]> InvalidCashAccountCases()
    {
        yield return [Guid.Empty, new ChartOfAccounts(), "cash_account_id is required."];

        var missingId = Guid.NewGuid();
        yield return [missingId, new ChartOfAccounts(), "Referenced cash / bank account is not available."];

        var liabilityId = Guid.NewGuid();
        yield return
        [
            liabilityId,
            CreateChart(CreateAccount(liabilityId, AccountType.Liability, StatementSection.Liabilities)),
            "Selected cash / bank account must be an asset account in the Assets section."
        ];

        var wrongSectionId = Guid.NewGuid();
        yield return
        [
            wrongSectionId,
            CreateChart(CreateAccount(wrongSectionId, AccountType.Asset, StatementSection.Expenses)),
            "Selected cash / bank account must be an asset account in the Assets section."
        ];

        var dimensionedId = Guid.NewGuid();
        yield return
        [
            dimensionedId,
            CreateChart(CreateAccount(dimensionedId, AccountType.Asset, StatementSection.Assets, requiresRequiredDimension: true)),
            "Selected cash / bank account must not require dimensions."
        ];
    }

    public static IEnumerable<object[]> ValidCashAccountCases()
    {
        yield return [CreateAccount(Guid.NewGuid(), AccountType.Asset, StatementSection.Assets)];
        yield return [CreateAccount(Guid.NewGuid(), AccountType.Asset, StatementSection.Assets, requiresRequiredDimension: false, includeOptionalDimension: true)];
        yield return [CreateAccount(Guid.NewGuid(), AccountType.Asset)];
    }

    private static ChartOfAccounts CreateChart(Account account)
    {
        var chart = new ChartOfAccounts();
        chart.Add(account);
        return chart;
    }

    private static Account CreateAccount(
        Guid id,
        AccountType type,
        StatementSection? section = null,
        bool requiresRequiredDimension = false,
        bool includeOptionalDimension = false)
    {
        IReadOnlyList<AccountDimensionRule>? dimensionRules = null;
        if (requiresRequiredDimension)
        {
            dimensionRules =
            [
                new AccountDimensionRule(Guid.NewGuid(), "department", 1, true)
            ];
        }
        else if (includeOptionalDimension)
        {
            dimensionRules =
            [
                new AccountDimensionRule(Guid.NewGuid(), "department", 1, false)
            ];
        }

        return new Account(
            id,
            code: $"101{id.ToString("N")[..4]}",
            name: "Cash account",
            type,
            statementSection: section,
            dimensionRules: dimensionRules);
    }
}

public sealed class TradeCatalogValidationGuards_P0Tests
{
    public delegate Task CatalogGuard(Guid id, string fieldPath, ICatalogService catalogs, CancellationToken ct);

    [Theory]
    [MemberData(nameof(ActiveCatalogGuardCases))]
    public async Task ActiveCatalogGuards_Reject_Empty_Ids(
        CatalogGuard guard,
        string fieldPath,
        string catalogType,
        string describedEntity)
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);

        Func<Task> act = () => guard(Guid.Empty, fieldPath, catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be(fieldPath);
        ex.Which.Reason.Should().Be($"{fieldPath} is required.");
        catalogs.Verify(
            x => x.GetByIdAsync(catalogType, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            $"empty ids should fail before {describedEntity} lookup");
    }

    [Theory]
    [MemberData(nameof(ActiveCatalogGuardCases))]
    public async Task ActiveCatalogGuards_Reject_Deleted_Items(
        CatalogGuard guard,
        string fieldPath,
        string catalogType,
        string describedEntity)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(catalogType, id, CreateCatalogItem(isDeleted: true));

        Func<Task> act = () => guard(id, fieldPath, catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be(fieldPath);
        ex.Which.Reason.Should().Be($"Referenced {describedEntity} is not available.");
    }

    [Theory]
    [MemberData(nameof(ActiveCatalogGuardCases))]
    public async Task ActiveCatalogGuards_Reject_Items_Marked_For_Deletion(
        CatalogGuard guard,
        string fieldPath,
        string catalogType,
        string describedEntity)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(catalogType, id, CreateCatalogItem(isMarkedForDeletion: true));

        Func<Task> act = () => guard(id, fieldPath, catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be(fieldPath);
        ex.Which.Reason.Should().Be($"Referenced {describedEntity} is not available.");
    }

    [Theory]
    [MemberData(nameof(ActiveCatalogGuardCases))]
    public async Task ActiveCatalogGuards_Reject_Inactive_Items(
        CatalogGuard guard,
        string fieldPath,
        string catalogType,
        string describedEntity)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            catalogType,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?> { ["is_active"] = false }));

        Func<Task> act = () => guard(id, fieldPath, catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be(fieldPath);
        ex.Which.Reason.Should().Be($"Referenced {describedEntity} is inactive.");
    }

    [Theory]
    [MemberData(nameof(ActiveCatalogGuardCases))]
    public async Task ActiveCatalogGuards_Accept_Active_Items_Encoded_As_Different_Primitives(
        CatalogGuard guard,
        string fieldPath,
        string catalogType,
        string _)
    {
        foreach (var activeValue in new object?[] { true, "true", 1 })
        {
            var fields = new Dictionary<string, object?>
            {
                ["is_active"] = activeValue
            };

            if (fieldPath == "vendor_id")
                fields["is_vendor"] = true;

            if (fieldPath == "customer_id")
                fields["is_customer"] = true;

            if (fieldPath == "lines[0].item_id")
                fields["is_inventory_item"] = true;

            var id = Guid.NewGuid();
            var catalogs = CreateCatalogService(
                catalogType,
                id,
                CreateCatalogItem(fields: fields));

            var act = () => guard(id, fieldPath, catalogs.Object, CancellationToken.None);

            await act.Should().NotThrowAsync($"{catalogType} should accept active flag encoded as {activeValue}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData("true")]
    [InlineData(1)]
    public async Task EnsureVendorAsync_Accepts_Active_Vendors_With_Different_Role_Encodings(object roleValue)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            TradeCodes.Party,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_vendor"] = roleValue
            }));

        var act = () => TradeCatalogValidationGuards.EnsureVendorAsync(id, "vendor_id", catalogs.Object, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData("false")]
    [InlineData(0)]
    public async Task EnsureVendorAsync_Rejects_When_Party_Is_Not_Marked_As_Vendor(object roleValue)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            TradeCodes.Party,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_vendor"] = roleValue
            }));

        Func<Task> act = () => TradeCatalogValidationGuards.EnsureVendorAsync(id, "vendor_id", catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("vendor_id");
        ex.Which.Reason.Should().Be("Selected business partner must be marked as a vendor.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData("true")]
    [InlineData(1)]
    public async Task EnsureCustomerAsync_Accepts_Active_Customers_With_Different_Role_Encodings(object roleValue)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            TradeCodes.Party,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_customer"] = roleValue
            }));

        var act = () => TradeCatalogValidationGuards.EnsureCustomerAsync(id, "customer_id", catalogs.Object, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData("false")]
    [InlineData(0)]
    public async Task EnsureCustomerAsync_Rejects_When_Party_Is_Not_Marked_As_Customer(object roleValue)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            TradeCodes.Party,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_customer"] = roleValue
            }));

        Func<Task> act = () => TradeCatalogValidationGuards.EnsureCustomerAsync(id, "customer_id", catalogs.Object, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("customer_id");
        ex.Which.Reason.Should().Be("Selected business partner must be marked as a customer.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData("true")]
    [InlineData(1)]
    public async Task EnsureInventoryItemAsync_Accepts_Items_With_Different_Inventory_Flag_Encodings(object inventoryValue)
    {
        var id = Guid.NewGuid();
        var catalogs = CreateCatalogService(
            TradeCodes.Item,
            id,
            CreateCatalogItem(fields: new Dictionary<string, object?>
            {
                ["is_active"] = true,
                ["is_inventory_item"] = inventoryValue
            }));

        var act = () => TradeCatalogValidationGuards.EnsureInventoryItemAsync(
            id,
            "lines[0].item_id",
            catalogs.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData("false")]
    [InlineData(0)]
    [InlineData(null)]
    public async Task EnsureInventoryItemAsync_Rejects_NonInventory_Items(object? inventoryValue)
    {
        var id = Guid.NewGuid();
        var fields = new Dictionary<string, object?>
        {
            ["is_active"] = true
        };
        if (inventoryValue is not null)
            fields["is_inventory_item"] = inventoryValue;

        var catalogs = CreateCatalogService(TradeCodes.Item, id, CreateCatalogItem(fields: fields));

        Func<Task> act = () => TradeCatalogValidationGuards.EnsureInventoryItemAsync(
            id,
            "lines[0].item_id",
            catalogs.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].item_id");
        ex.Which.Reason.Should().Be("Selected item must be marked as an inventory item.");
    }

    public static IEnumerable<object[]> ActiveCatalogGuardCases()
    {
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsureVendorAsync, "vendor_id", TradeCodes.Party, "business partner"];
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsureCustomerAsync, "customer_id", TradeCodes.Party, "business partner"];
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsureWarehouseAsync, "warehouse_id", TradeCodes.Warehouse, "warehouse"];
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsureInventoryItemAsync, "lines[0].item_id", TradeCodes.Item, "item"];
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsurePriceTypeAsync, "price_type_id", TradeCodes.PriceType, "price type"];
        yield return [(CatalogGuard)TradeCatalogValidationGuards.EnsureInventoryAdjustmentReasonAsync, "reason_id", TradeCodes.InventoryAdjustmentReason, "inventory adjustment reason"];
    }

    private static Mock<ICatalogService> CreateCatalogService(string catalogType, Guid id, CatalogItemDto item)
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs
            .Setup(x => x.GetByIdAsync(catalogType, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        return catalogs;
    }

    private static CatalogItemDto CreateCatalogItem(
        IReadOnlyDictionary<string, object?>? fields = null,
        bool isMarkedForDeletion = false,
        bool isDeleted = false)
    {
        IReadOnlyDictionary<string, JsonElement>? payloadFields = null;
        if (fields is not null)
        {
            payloadFields = fields.ToDictionary(
                static pair => pair.Key,
                static pair => JsonSerializer.SerializeToElement(pair.Value));
        }

        return new CatalogItemDto(
            Guid.NewGuid(),
            "Catalog Item",
            new RecordPayload(payloadFields, null),
            isMarkedForDeletion,
            isDeleted);
    }
}

public sealed class TradeDocumentReferenceValidationGuards_P0Tests
{
    [Fact]
    public async Task EnsurePostedSalesInvoiceAsync_Rejects_Missing_Document()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice is not available.");
    }

    [Fact]
    public async Task EnsurePostedSalesInvoiceAsync_Rejects_Wrong_Document_Type()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.PurchaseReceipt, DocumentStatus.Posted));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice is not available.");
    }

    [Fact]
    public async Task EnsurePostedSalesInvoiceAsync_Rejects_Draft_Document()
    {
        var documentId = Guid.NewGuid();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.SalesInvoice, DocumentStatus.Draft, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
            documentId,
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must be posted.");
    }

    [Fact]
    public async Task EnsurePostedSalesInvoiceAsync_Rejects_Customer_Mismatch()
    {
        var documentId = Guid.NewGuid();
        var expectedCustomerId = Guid.NewGuid();
        var actualCustomerId = Guid.NewGuid();

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.SalesInvoice, DocumentStatus.Posted, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers
            .Setup(x => x.ReadSalesInvoiceHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeSalesInvoiceHead(
                documentId,
                new DateOnly(2026, 4, 18),
                actualCustomerId,
                Guid.NewGuid(),
                null,
                null,
                10m));

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
            documentId,
            expectedCustomerId,
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must belong to the selected customer.");
    }

    [Fact]
    public async Task EnsurePostedSalesInvoiceAsync_Allows_Posted_Document_For_Selected_Customer()
    {
        var documentId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.SalesInvoice, DocumentStatus.Posted, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers
            .Setup(x => x.ReadSalesInvoiceHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeSalesInvoiceHead(
                documentId,
                new DateOnly(2026, 4, 18),
                customerId,
                Guid.NewGuid(),
                null,
                null,
                10m));

        var act = () => TradeDocumentReferenceValidationGuards.EnsurePostedSalesInvoiceAsync(
            documentId,
            customerId,
            readers.Object,
            documents.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsurePostedPurchaseReceiptAsync_Rejects_Missing_Document()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt is not available.");
    }

    [Fact]
    public async Task EnsurePostedPurchaseReceiptAsync_Rejects_Wrong_Document_Type()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.SalesInvoice, DocumentStatus.Posted));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt is not available.");
    }

    [Fact]
    public async Task EnsurePostedPurchaseReceiptAsync_Rejects_Draft_Document()
    {
        var documentId = Guid.NewGuid();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.PurchaseReceipt, DocumentStatus.Draft, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
            documentId,
            Guid.NewGuid(),
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must be posted.");
    }

    [Fact]
    public async Task EnsurePostedPurchaseReceiptAsync_Rejects_Vendor_Mismatch()
    {
        var documentId = Guid.NewGuid();
        var expectedVendorId = Guid.NewGuid();
        var actualVendorId = Guid.NewGuid();

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.PurchaseReceipt, DocumentStatus.Posted, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                documentId,
                new DateOnly(2026, 4, 18),
                actualVendorId,
                Guid.NewGuid(),
                null,
                10m));

        Func<Task> act = () => TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
            documentId,
            expectedVendorId,
            readers.Object,
            documents.Object,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must belong to the selected vendor.");
    }

    [Fact]
    public async Task EnsurePostedPurchaseReceiptAsync_Allows_Posted_Document_For_Selected_Vendor()
    {
        var documentId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents
            .Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument(TradeCodes.PurchaseReceipt, DocumentStatus.Posted, documentId));

        var readers = new Mock<ITradeDocumentReaders>(MockBehavior.Strict);
        readers
            .Setup(x => x.ReadPurchaseReceiptHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradePurchaseReceiptHead(
                documentId,
                new DateOnly(2026, 4, 18),
                vendorId,
                Guid.NewGuid(),
                null,
                10m));

        var act = () => TradeDocumentReferenceValidationGuards.EnsurePostedPurchaseReceiptAsync(
            documentId,
            vendorId,
            readers.Object,
            documents.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static DocumentRecord CreateDocument(string typeCode, DocumentStatus status, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TypeCode = typeCode,
            Status = status,
            DateUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
}
