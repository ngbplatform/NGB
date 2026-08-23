using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using NGB.Trade.Migrator.Seed;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

public sealed class TradeSeedDemoCliFullCoverageTests
{
    private static readonly DateOnly Today = new(2026, 8, 22);

    [Fact]
    public void Command_detection_trimming_defaults_summary_and_conflict_contract_are_stable()
    {
        TradeSeedDemoCli.IsSeedDemoCommand([]).Should().BeFalse();
        TradeSeedDemoCli.IsSeedDemoCommand(["other"]).Should().BeFalse();
        TradeSeedDemoCli.IsSeedDemoCommand(["SEED-DEMO"]).Should().BeTrue();
        TradeSeedDemoCli.TrimCommand([]).Should().BeEmpty();
        TradeSeedDemoCli.TrimCommand(["seed-demo"]).Should().BeEmpty();
        TradeSeedDemoCli.TrimCommand(["seed-demo", "--seed", "7"]).Should().Equal("--seed", "7");

        var options = TradeDemoSeedOptions.Parse(["--connection=test"], Today);
        options.Should().BeEquivalentTo(new
        {
            ConnectionString = "test",
            Seed = 20260411,
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = Today,
            Warehouses = 4,
            Customers = 18,
            Vendors = 12,
            Items = 36,
            PriceUpdates = 12,
            PurchaseReceipts = 48,
            SalesInvoices = 72,
            CustomerPayments = 54,
            VendorPayments = 36,
            InventoryTransfers = 24,
            InventoryAdjustments = 18,
            CustomerReturns = 12,
            VendorReturns = 10,
            ClosePeriods = true,
            SkipIfActivityExists = false
        });

        var summary = new TradeDemoSeedSummary(
            new DateOnly(2026, 1, 1),
            Today,
            1, 2, 3, 4,
            1, 2, 3, 4, 5, 6, 7, 8, 9,
            10, 11);
        summary.TotalDocumentsPosted.Should().Be(45);
        new TradeSeedActivityAlreadyExistsException().ErrorCode.Should()
            .Be(TradeSeedActivityAlreadyExistsException.ErrorCodeConst);
    }

    [Fact]
    public void Explicit_valid_options_include_boolean_and_numeric_boundaries()
    {
        var options = TradeDemoSeedOptions.Parse([
            "--connection", "test",
            "--seed", "-1",
            "--from", "2026-01-01",
            "--to", "2026-01-01",
            "--warehouses", "24",
            "--customers", "500",
            "--vendors", "250",
            "--items", "1000",
            "--price-updates", "500",
            "--purchase-receipts", "20000",
            "--sales-invoices", "20000",
            "--customer-payments", "20000",
            "--vendor-payments", "20000",
            "--inventory-transfers", "20000",
            "--inventory-adjustments", "20000",
            "--customer-returns", "10000",
            "--vendor-returns", "10000",
            "--close-periods", "false",
            "--skip-if-activity-exists", "true"
        ], Today);

        options.Seed.Should().Be(-1);
        options.FromDate.Should().Be(options.ToDate);
        options.ClosePeriods.Should().BeFalse();
        options.SkipIfActivityExists.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void Every_numeric_option_rejects_values_outside_its_supported_range(string option, int value)
    {
        Action action = () => TradeDemoSeedOptions.Parse([
            "--connection=test",
            option, value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ], Today);

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(option);
    }

    [Fact]
    public async Task Reversed_dates_and_invalid_cli_values_fail_before_service_provider_or_database_creation()
    {
        Action reversed = () => TradeDemoSeedOptions.Parse([
            "--connection=test", "--from=2026-08-23", "--to=2026-08-22"
        ], Today);
        reversed.Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("--from");

        var exitCode = await TradeSeedDemoCli.RunAsync(
            ["--connection=test", "--warehouses=0"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public void Pure_seed_helpers_cover_collection_inventory_date_and_random_boundaries()
    {
        var seeder = CreateSeeder();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        seeder.BuildCompanyName(["Acme"], ["Labs"], 0, used).Should().Be("Acme Labs");
        seeder.BuildCompanyName(["Acme"], ["Labs"], 1, used).Should().Be("Acme Labs 2");

        seeder.PickDistinctItems(new[] { 1, 2 }, 2).Should().Equal(1, 2);
        seeder.PickDistinctItems(new[] { 1, 2, 3 }, 1).Should().ContainSingle();

        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var firstDate = new DateOnly(2026, 8, 20);
        var secondDate = firstDate.AddDays(1);
        seeder.GetInventory(warehouseId, itemId, firstDate).Should().Be(0m);
        seeder.AddInventoryDelta(warehouseId, itemId, 0m, firstDate);
        seeder.AddInventory(warehouseId, itemId, 5m, firstDate);
        seeder.AddInventoryDelta(warehouseId, itemId, -2m, firstDate);
        seeder.AddInventory(warehouseId, itemId, 4m, secondDate);
        seeder.GetInventory(warehouseId, itemId, firstDate).Should().Be(3m);
        seeder.GetInventory(warehouseId, itemId, secondDate).Should().Be(7m);
        seeder.RemoveInventory(warehouseId, itemId, 99m, firstDate);
        seeder.GetInventory(warehouseId, itemId, firstDate).Should().Be(0m);

        seeder.BuildSpreadDates(0, firstDate, secondDate).Should().BeEmpty();
        seeder.BuildSpreadDates(1, firstDate, secondDate).Should().Equal(firstDate);
        seeder.BuildSpreadDates(2, firstDate, firstDate).Should().Equal(firstDate, firstDate);
        seeder.BuildSpreadDates(3, firstDate, firstDate.AddDays(2)).Should()
            .OnlyContain(x => x >= firstDate && x <= firstDate.AddDays(2));
        seeder.BuildSpreadDates(3, firstDate, firstDate.AddDays(30)).Should()
            .BeInAscendingOrder();

        seeder.RandomDate(firstDate, firstDate).Should().Be(firstDate);
        seeder.RandomDate(firstDate, secondDate).Should().BeOnOrAfter(firstDate).And.BeOnOrBefore(secondDate);
        seeder.RandomLaterDate(firstDate, secondDate).Should().Be(secondDate);
        seeder.RandomLaterDate(secondDate, secondDate).Should().Be(secondDate);
        seeder.RandomLaterDate(secondDate.AddDays(1), secondDate).Should().Be(secondDate);
        seeder.RandomFactor(0.9m, 1.1m).Should().BeInRange(0.9m, 1.1m);
        TradeDemoSeeder.RoundMoney(1.005m).Should().Be(1.01m);

        var notes = Enumerable.Range(0, 100).Select(_ => seeder.MaybeNote(["note"])).ToArray();
        notes.Should().Contain(x => x == null).And.Contain("note");
        TradeDemoSeeder.Payload(new { value = 7 }).Fields.Should().ContainKey("value");
    }

    [Fact]
    public async Task Seeder_dependency_edges_cover_accounts_catalogs_documents_and_preclosed_periods()
    {
        var accountId = Guid.NewGuid();
        var account = new Account(
            accountId,
            "3200",
            "Retained Earnings",
            AccountType.Equity,
            StatementSection.Equity);
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.SetupSequence(x => x.GetAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChartOfAccountsAdminItem { Account = account, IsActive = false, IsDeleted = true }])
            .ReturnsAsync([new ChartOfAccountsAdminItem { Account = account, IsActive = true, IsDeleted = false }])
            .ReturnsAsync([]);
        var management = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        management.Setup(x => x.UnmarkForDeletionAsync(accountId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        management.Setup(x => x.SetActiveAsync(accountId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var createdAccountId = Guid.NewGuid();
        management.Setup(x => x.CreateAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAccountId);
        var accountSeeder = CreateSeeder(chartAdmin: admin.Object, chartManagement: management.Object);

        (await accountSeeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(accountId);
        (await accountSeeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(accountId);
        (await accountSeeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(createdAccountId);

        var existingCatalogId = Guid.NewGuid();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetPageAsync(
                "catalog", It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([Catalog(existingCatalogId, "Existing")], 0, 50, 1))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([], 0, 50, 0))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [Catalog(Guid.NewGuid(), "Duplicate"), Catalog(Guid.NewGuid(), "duplicate")], 0, 50, 2));
        var catalogSeeder = CreateSeeder(catalogs: catalogs.Object);
        (await catalogSeeder.GetCatalogIdByDisplayAsync(
            "catalog", "Existing", CancellationToken.None)).Should().Be(existingCatalogId);
        await FluentActions.Awaiting(() => catalogSeeder.GetCatalogIdByDisplayAsync(
                "catalog", "Missing", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await FluentActions.Awaiting(() => catalogSeeder.GetCatalogIdByDisplayAsync(
                "catalog", "Duplicate", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var documentSeeder = CreateSeeder(
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        var posted = await documentSeeder.CreateAndPostAsync(
            "document", Today, new RecordPayload(), CancellationToken.None);
        posted.Status.Should().Be(DocumentStatus.Posted);

        var period = new DateOnly(2024, 1, 1);
        var closedReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedReader.Setup(x => x.GetClosedAsync(period, period, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClosedPeriodRecord { Period = period, ClosedBy = "test", ClosedAtUtc = DateTime.UnixEpoch }]);
        var periodClosing = new Mock<IPeriodClosingService>(MockBehavior.Strict);
        var periodSeeder = CreateSeeder(
            fromDate: period,
            toDate: period,
            closePeriods: true,
            periodClosing: periodClosing.Object,
            closedPeriodReader: closedReader.Object,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var closingSummary = await periodSeeder.SeedPeriodClosingsAsync(Guid.NewGuid(), CancellationToken.None);
        closingSummary.Should().Be(new TradeDemoSeeder.PeriodClosingSummary(0, 0));
        periodClosing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Seeder_generation_edges_cover_depleted_and_missing_inventory_paths()
    {
        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var seeder = CreateSeeder(
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        var itemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var item = new TradeDemoSeeder.ItemSeedResult(itemId, 10m, 15m);
        var warehouse = new TradeDemoSeeder.WarehouseSeedResult(warehouseId, "Warehouse", "WH", "Address");
        var customer = new TradeDemoSeeder.PartySeedResult(customerId, "Customer");

        (await seeder.SeedSalesInvoicesAsync(
            [item], [customer], [warehouse], Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();
        (await seeder.SeedInventoryTransfersAsync(
            [item], [warehouse], CancellationToken.None)).Should().Be(0);
        seeder.AddInventory(warehouseId, itemId, 10m, Today.AddDays(-10));
        (await seeder.SeedInventoryTransfersAsync(
            [item], [warehouse], CancellationToken.None)).Should().Be(0);

        var exhaustedSale = new TradeDemoSeeder.SalesInvoiceSeedResult(
            Guid.NewGuid(),
            customerId,
            warehouseId,
            Today.AddDays(-1),
            15m,
            [new TradeDemoSeeder.SalesInvoiceLineState(itemId, 15m, 10m, 0m)]);
        (await seeder.SeedCustomerReturnsAsync([exhaustedSale], CancellationToken.None)).Should().Be(1);

        var vendorId = Guid.NewGuid();
        var exhaustedReceipt = new TradeDemoSeeder.PurchaseReceiptSeedResult(
            Guid.NewGuid(),
            vendorId,
            warehouseId,
            Today.AddDays(-1),
            10m,
            [new TradeDemoSeeder.PurchaseReceiptLineState(itemId, 10m, 0m)]);
        seeder.AddInventory(warehouseId, itemId, 2m, Today);
        (await seeder.SeedVendorReturnsAsync([exhaustedReceipt], CancellationToken.None)).Should().Be(1);

        var missingItemId = Guid.NewGuid();
        var unavailableReceipt = new TradeDemoSeeder.PurchaseReceiptSeedResult(
            Guid.NewGuid(),
            vendorId,
            warehouseId,
            Today.AddDays(-1),
            10m,
            [new TradeDemoSeeder.PurchaseReceiptLineState(missingItemId, 10m, 1m)]);
        (await seeder.SeedVendorReturnsAsync([unavailableReceipt], CancellationToken.None)).Should().Be(0);

        var undatedReceipt = new TradeDemoSeeder.PurchaseReceiptSeedResult(
            Guid.NewGuid(),
            vendorId,
            warehouseId,
            Today,
            10m,
            [new TradeDemoSeeder.PurchaseReceiptLineState(Guid.NewGuid(), 10m, 1m)]);
        (await seeder.SeedVendorReturnsAsync([undatedReceipt], CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Catalog_series_adjustment_and_payment_predicates_cover_both_sides()
    {
        var catalogs = new Mock<ICatalogService>();
        catalogs.Setup(x => x.CreateAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(Guid.NewGuid(), "Created"));
        var catalogSeeder = CreateSeeder(warehouses: 9, items: 31, catalogs: catalogs.Object);

        var warehouses = await catalogSeeder.SeedWarehousesAsync(CancellationToken.None);
        var items = await catalogSeeder.SeedItemsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        warehouses.Should().HaveCount(9);
        warehouses[^1].Display.Should().EndWith(" 3");
        warehouses[^1].Code.Should().EndWith("2");
        items.Should().HaveCount(31);

        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var item = new TradeDemoSeeder.ItemSeedResult(Guid.NewGuid(), 10m, 15m);
        var warehouse = new TradeDemoSeeder.WarehouseSeedResult(Guid.NewGuid(), "Warehouse", "WH", "Address");
        var lowInventorySeeder = CreateSeeder(
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        (await lowInventorySeeder.SeedInventoryAdjustmentsAsync(
            [item], [warehouse], Guid.NewGuid(), CancellationToken.None)).Should().Be(1);

        for (var seed = 0; seed < 100; seed++)
        {
            var highInventorySeeder = CreateSeeder(
                seed: seed,
                documents: documents.Object,
                lifecycle: lifecycle.Object,
                drafts: drafts.Object);
            highInventorySeeder.AddInventory(warehouse.Id, item.Id, 10m, Today.AddDays(-10));
            (await highInventorySeeder.SeedInventoryAdjustmentsAsync(
                [item], [warehouse], Guid.NewGuid(), CancellationToken.None)).Should().Be(1);
        }

        var zeroInvoice = new TradeDemoSeeder.SalesInvoiceSeedResult(
            Guid.NewGuid(), Guid.NewGuid(), warehouse.Id, Today.AddDays(-1), 100m,
            [new TradeDemoSeeder.SalesInvoiceLineState(item.Id, 15m, 10m, 1m)])
        {
            OutstandingAmount = 0m
        };
        var zeroReceipt = new TradeDemoSeeder.PurchaseReceiptSeedResult(
            Guid.NewGuid(), Guid.NewGuid(), warehouse.Id, Today.AddDays(-1), 100m,
            [new TradeDemoSeeder.PurchaseReceiptLineState(item.Id, 10m, 1m)])
        {
            OutstandingAmount = 0m
        };
        (await lowInventorySeeder.SeedCustomerPaymentsAsync([zeroInvoice], CancellationToken.None)).Should().Be(1);
        (await lowInventorySeeder.SeedVendorPaymentsAsync([zeroReceipt], CancellationToken.None)).Should().Be(1);
    }

    private static TradeDemoSeeder CreateSeeder(
        int seed = 20260823,
        int warehouses = 1,
        int items = 1,
        ICatalogService? catalogs = null,
        IDocumentService? documents = null,
        IDocumentSystemLifecycleService? lifecycle = null,
        IDocumentDraftService? drafts = null,
        IChartOfAccountsAdminService? chartAdmin = null,
        IChartOfAccountsManagementService? chartManagement = null,
        IPeriodClosingService? periodClosing = null,
        IClosedPeriodReader? closedPeriodReader = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        bool closePeriods = false,
        TimeProvider? timeProvider = null)
        => new(
            new TradeDemoSeedOptions(
                "test", seed,
                fromDate ?? Today.AddDays(-10),
                toDate ?? Today,
                warehouses, 1, 1, items, 1, 1, 1, 1, 1, 1, 1, 1, 1,
                closePeriods,
                false),
            catalogs ?? Mock.Of<ICatalogService>(),
            documents ?? Mock.Of<IDocumentService>(),
            lifecycle ?? Mock.Of<IDocumentSystemLifecycleService>(),
            drafts ?? Mock.Of<IDocumentDraftService>(),
            chartAdmin ?? Mock.Of<IChartOfAccountsAdminService>(),
            chartManagement ?? Mock.Of<IChartOfAccountsManagementService>(),
            periodClosing ?? Mock.Of<IPeriodClosingService>(),
            closedPeriodReader ?? Mock.Of<IClosedPeriodReader>(),
            timeProvider ?? TimeProvider.System);

    private static Mock<IDocumentService> DocumentMocks(
        out Mock<IDocumentSystemLifecycleService> lifecycle,
        out Mock<IDocumentDraftService> drafts)
    {
        var documents = new Mock<IDocumentService>();
        documents.Setup(x => x.CreateDraftAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, RecordPayload _, CancellationToken _) =>
                Document(Guid.NewGuid(), DocumentStatus.Draft));
        drafts = new Mock<IDocumentDraftService>();
        drafts.Setup(x => x.UpdateDraftAsync(
                It.IsAny<Guid>(), null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        lifecycle = new Mock<IDocumentSystemLifecycleService>();
        lifecycle.Setup(x => x.PostAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) => Document(id, DocumentStatus.Posted));
        return documents;
    }

    private static CatalogItemDto Catalog(Guid id, string display)
        => new(id, display, new RecordPayload(), false, false);

    private static DocumentDto Document(Guid id, DocumentStatus status)
        => new(id, null, new RecordPayload(), status, false);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    public static TheoryData<string, int> InvalidRanges => new()
    {
        { "--warehouses", 0 }, { "--warehouses", 25 },
        { "--customers", 0 }, { "--customers", 501 },
        { "--vendors", 0 }, { "--vendors", 251 },
        { "--items", 0 }, { "--items", 1001 },
        { "--price-updates", 0 }, { "--price-updates", 501 },
        { "--purchase-receipts", 3 }, { "--purchase-receipts", 20001 },
        { "--sales-invoices", 0 }, { "--sales-invoices", 20001 },
        { "--customer-payments", 0 }, { "--customer-payments", 20001 },
        { "--vendor-payments", 0 }, { "--vendor-payments", 20001 },
        { "--inventory-transfers", 0 }, { "--inventory-transfers", 20001 },
        { "--inventory-adjustments", 0 }, { "--inventory-adjustments", 20001 },
        { "--customer-returns", 0 }, { "--customer-returns", 10001 },
        { "--vendor-returns", 0 }, { "--vendor-returns", 10001 }
    };
}
