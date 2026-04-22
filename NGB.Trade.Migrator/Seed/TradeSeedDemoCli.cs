using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Persistence.Readers.Periods;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Accounts;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime;
using NGB.Trade.Runtime.DependencyInjection;

namespace NGB.Trade.Migrator.Seed;

internal static class TradeSeedDemoCli
{
    private const string CommandName = "seed-demo";

    public static bool IsSeedDemoCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args, TimeProvider? timeProvider = null)
    {
        TradeDemoSeedOptions? options = null;

        try
        {
            var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
            options = TradeDemoSeedOptions.Parse(args, DateOnly.FromDateTime(effectiveTimeProvider.GetUtcNow().UtcDateTime));

            var services = new ServiceCollection();
            services.AddLogging();

            services
                .AddNgbRuntime()
                .AddNgbPostgres(options.ConnectionString)
                .AddTradeModule()
                .AddTradeRuntimeModule()
                .AddTradePostgresModule();
            services.AddSingleton(effectiveTimeProvider);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using (var setupScope = provider.CreateAsyncScope())
            {
                var setupService = setupScope.ServiceProvider.GetRequiredService<ITradeSetupService>();
                await setupService.EnsureDefaultsAsync();
            }

            await using var seedScope = provider.CreateAsyncScope();
            var seeder = new TradeDemoSeeder(
                options,
                seedScope.ServiceProvider.GetRequiredService<ICatalogService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentDraftService>(),
                seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>(),
                seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>(),
                seedScope.ServiceProvider.GetRequiredService<IPeriodClosingService>(),
                seedScope.ServiceProvider.GetRequiredService<IClosedPeriodReader>(),
                effectiveTimeProvider);

            var summary = await seeder.RunAsync();
            PrintSummary(summary);
            return 0;
        }
        catch (TradeSeedActivityAlreadyExistsException) when (options?.SkipIfActivityExists == true)
        {
            Console.WriteLine("OK: trade demo seed skipped because activity already exists.");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: trade seed-demo error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintSummary(TradeDemoSeedSummary summary)
    {
        Console.WriteLine("OK: trade historical data seeded.");
        Console.WriteLine($"- Period: {summary.FromDate:yyyy-MM-dd} .. {summary.ToDate:yyyy-MM-dd}");
        Console.WriteLine($"- Warehouses created: {summary.WarehousesCreated}");
        Console.WriteLine($"- Customer parties created: {summary.CustomersCreated}");
        Console.WriteLine($"- Vendor parties created: {summary.VendorsCreated}");
        Console.WriteLine($"- Items created: {summary.ItemsCreated}");
        Console.WriteLine($"- Item Price Update documents posted: {summary.PriceUpdatesPosted}");
        Console.WriteLine($"- Purchase Receipt documents posted: {summary.PurchaseReceiptsPosted}");
        Console.WriteLine($"- Sales Invoice documents posted: {summary.SalesInvoicesPosted}");
        Console.WriteLine($"- Customer Payment documents posted: {summary.CustomerPaymentsPosted}");
        Console.WriteLine($"- Vendor Payment documents posted: {summary.VendorPaymentsPosted}");
        Console.WriteLine($"- Inventory Transfer documents posted: {summary.InventoryTransfersPosted}");
        Console.WriteLine($"- Inventory Adjustment documents posted: {summary.InventoryAdjustmentsPosted}");
        Console.WriteLine($"- Customer Return documents posted: {summary.CustomerReturnsPosted}");
        Console.WriteLine($"- Vendor Return documents posted: {summary.VendorReturnsPosted}");
        Console.WriteLine($"- Total trade documents posted: {summary.TotalDocumentsPosted}");
        Console.WriteLine($"- Months closed: {summary.MonthsClosed}");
        Console.WriteLine($"- Fiscal years closed: {summary.FiscalYearsClosed}");
    }
}

internal sealed class TradeSeedActivityAlreadyExistsException()
    : NgbConflictException(
        message: "Trade seed-demo expects a clean Trade activity ledger. Existing Trade documents were found.",
        errorCode: ErrorCodeConst)
{
    public const string ErrorCodeConst = "trd.seed_demo.activity_exists";
}

internal sealed record TradeDemoSeedOptions(
    string ConnectionString,
    int Seed,
    DateOnly FromDate,
    DateOnly ToDate,
    int Warehouses,
    int Customers,
    int Vendors,
    int Items,
    int PriceUpdates,
    int PurchaseReceipts,
    int SalesInvoices,
    int CustomerPayments,
    int VendorPayments,
    int InventoryTransfers,
    int InventoryAdjustments,
    int CustomerReturns,
    int VendorReturns,
    bool ClosePeriods,
    bool SkipIfActivityExists)
{
    public static TradeDemoSeedOptions Parse(string[] args, DateOnly todayUtc)
    {
        var connectionString = TradeSeedCliArgs.RequireConnectionString(args);
        var seed = TradeSeedCliArgs.GetInt(args, "--seed", 20260411);
        var fromDate = TradeSeedCliArgs.GetDateOnly(args, "--from", new DateOnly(2024, 1, 1));
        var toDate = TradeSeedCliArgs.GetDateOnly(args, "--to", todayUtc);
        var warehouses = TradeSeedCliArgs.GetInt(args, "--warehouses", 4);
        var customers = TradeSeedCliArgs.GetInt(args, "--customers", 18);
        var vendors = TradeSeedCliArgs.GetInt(args, "--vendors", 12);
        var items = TradeSeedCliArgs.GetInt(args, "--items", 36);
        var priceUpdates = TradeSeedCliArgs.GetInt(args, "--price-updates", 12);
        var purchaseReceipts = TradeSeedCliArgs.GetInt(args, "--purchase-receipts", 48);
        var salesInvoices = TradeSeedCliArgs.GetInt(args, "--sales-invoices", 72);
        var customerPayments = TradeSeedCliArgs.GetInt(args, "--customer-payments", 54);
        var vendorPayments = TradeSeedCliArgs.GetInt(args, "--vendor-payments", 36);
        var inventoryTransfers = TradeSeedCliArgs.GetInt(args, "--inventory-transfers", 24);
        var inventoryAdjustments = TradeSeedCliArgs.GetInt(args, "--inventory-adjustments", 18);
        var customerReturns = TradeSeedCliArgs.GetInt(args, "--customer-returns", 12);
        var vendorReturns = TradeSeedCliArgs.GetInt(args, "--vendor-returns", 10);
        var closePeriods = TradeSeedCliArgs.GetBool(args, "--close-periods", true);
        var skipIfActivityExists = TradeSeedCliArgs.GetBool(args, "--skip-if-activity-exists", false);

        if (fromDate > toDate)
            throw new NgbArgumentInvalidException("--from", "'--from' must be less than or equal to '--to'.");

        ValidateRange("--warehouses", warehouses, 1, 24);
        ValidateRange("--customers", customers, 1, 500);
        ValidateRange("--vendors", vendors, 1, 250);
        ValidateRange("--items", items, 1, 1000);
        ValidateRange("--price-updates", priceUpdates, 1, 500);
        ValidateRange("--purchase-receipts", purchaseReceipts, warehouses, 20000);
        ValidateRange("--sales-invoices", salesInvoices, 1, 20000);
        ValidateRange("--customer-payments", customerPayments, 1, 20000);
        ValidateRange("--vendor-payments", vendorPayments, 1, 20000);
        ValidateRange("--inventory-transfers", inventoryTransfers, 1, 20000);
        ValidateRange("--inventory-adjustments", inventoryAdjustments, 1, 20000);
        ValidateRange("--customer-returns", customerReturns, 1, 10000);
        ValidateRange("--vendor-returns", vendorReturns, 1, 10000);

        return new TradeDemoSeedOptions(
            connectionString,
            seed,
            fromDate,
            toDate,
            warehouses,
            customers,
            vendors,
            items,
            priceUpdates,
            purchaseReceipts,
            salesInvoices,
            customerPayments,
            vendorPayments,
            inventoryTransfers,
            inventoryAdjustments,
            customerReturns,
            vendorReturns,
            closePeriods,
            skipIfActivityExists);
    }

    private static void ValidateRange(string name, int value, int min, int max)
    {
        if (value < min || value > max)
            throw new NgbArgumentOutOfRangeException(name, value, $"'{name}' must be between {min} and {max}.");
    }
}

internal sealed record TradeDemoSeedSummary(
    DateOnly FromDate,
    DateOnly ToDate,
    int WarehousesCreated,
    int CustomersCreated,
    int VendorsCreated,
    int ItemsCreated,
    int PriceUpdatesPosted,
    int PurchaseReceiptsPosted,
    int SalesInvoicesPosted,
    int CustomerPaymentsPosted,
    int VendorPaymentsPosted,
    int InventoryTransfersPosted,
    int InventoryAdjustmentsPosted,
    int CustomerReturnsPosted,
    int VendorReturnsPosted,
    int MonthsClosed,
    int FiscalYearsClosed)
{
    public int TotalDocumentsPosted =>
        PriceUpdatesPosted
        + PurchaseReceiptsPosted
        + SalesInvoicesPosted
        + CustomerPaymentsPosted
        + VendorPaymentsPosted
        + InventoryTransfersPosted
        + InventoryAdjustmentsPosted
        + CustomerReturnsPosted
        + VendorReturnsPosted;
}

internal sealed class TradeDemoSeeder(
    TradeDemoSeedOptions options,
    ICatalogService catalogs,
    IDocumentService documents,
    IDocumentDraftService drafts,
    IChartOfAccountsAdminService chartOfAccountsAdmin,
    IChartOfAccountsManagementService chartOfAccountsManagement,
    IPeriodClosingService periodClosing,
    IClosedPeriodReader closedPeriodReader,
    TimeProvider timeProvider)
{
    private readonly Random _random = new(options.Seed);
    private readonly Dictionary<(Guid WarehouseId, Guid ItemId), SortedDictionary<DateOnly, decimal>> _inventory = [];
    private readonly Dictionary<Guid, decimal> _currentRetailPrices = [];

    private static readonly WarehouseTemplate[] WarehouseTemplates =
    [
        new("ATL", "Southeast Distribution Center", "1450 Fulton Industrial Blvd NW, Atlanta, GA"),
        new("DFW", "South Central Distribution Center", "8200 Sterling St, Irving, TX"),
        new("MIA", "Florida Fulfillment Center", "100 Harbor Blvd, Miami, FL"),
        new("PHX", "Desert West Logistics Hub", "4100 W Buckeye Rd, Phoenix, AZ"),
        new("CHI", "Great Lakes Distribution Center", "2550 Busse Rd, Elk Grove Village, IL"),
        new("RNO", "Sierra Transit Warehouse", "9750 Lear Blvd, Reno, NV"),
        new("CLT", "Carolinas Regional Warehouse", "8600 Wilkinson Blvd, Charlotte, NC"),
        new("CBUS", "Midwest Commerce Center", "3720 Gantz Rd, Grove City, OH")
    ];

    private static readonly (string City, string State)[] PartyCities =
    [
        ("New York", "NY"),
        ("Chicago", "IL"),
        ("Atlanta", "GA"),
        ("Dallas", "TX"),
        ("Miami", "FL"),
        ("Phoenix", "AZ"),
        ("Denver", "CO"),
        ("Columbus", "OH"),
        ("Nashville", "TN"),
        ("Raleigh", "NC"),
        ("Tampa", "FL"),
        ("Kansas City", "MO"),
        ("Salt Lake City", "UT"),
        ("Indianapolis", "IN")
    ];

    private static readonly string[] StreetNames =
    [
        "Commerce", "Market", "Harbor", "Ridge", "Pioneer", "Meridian", "Summit", "Oak", "Cedar", "Lakeview",
        "Broadway", "Industrial", "Exchange", "Mason", "Westport", "Liberty", "Bayshore", "Grant", "Peachtree", "Maple"
    ];

    private static readonly string[] CustomerPrefixes =
    [
        "Harbor Point", "Northgate", "Redwood", "Blue Ridge", "Summit", "Great Lakes", "Pioneer", "Cedar Lane", "Metro",
        "Prairie", "Silver Oak", "Evergreen", "Southline", "Seacoast", "Ironwood", "Granite Bay", "Mesa", "Clearwater"
    ];

    private static readonly string[] CustomerSuffixes =
    [
        "Retail Group", "Home Goods", "Office Supply", "Hardware", "Outfitters", "Wholesale Mart", "Store Services",
        "Kitchen & Bath", "Building Supply", "Industrial Supply", "Farm & Fleet", "Auto Parts", "Outdoor Supply", "Workwear"
    ];

    private static readonly string[] VendorPrefixes =
    [
        "Atlas", "Beacon", "Blue Horizon", "Cardinal", "Evergreen", "Frontier", "Granite", "Keystone", "Liberty",
        "Meridian", "Northstar", "Oakline", "Pacific Crest", "Riverton", "Silverline", "Tidewater", "Union", "Westbridge"
    ];

    private static readonly string[] VendorSuffixes =
    [
        "Industrial Supply", "Packaging Co.", "Electric Supply", "Facilities Supply", "Restaurant Supply", "Office Products",
        "Distribution", "Logistics Supply", "Wholesale Partners", "Safety Products", "Janitorial Supply", "Warehouse Supply"
    ];

    private static readonly ItemTemplate[] ItemTemplates =
    [
        new("Wire Shelf Rack 48 x 18", "WSR", 68m, 1.55m),
        new("Storage Tote 27 Gallon", "TOTE", 14m, 1.85m),
        new("Extension Cord 25 ft", "CORD", 9m, 1.90m),
        new("Folding Table 6 ft", "TABLE", 42m, 1.55m),
        new("Adjustable Monitor Arm", "MARM", 38m, 1.75m),
        new("USB-C Docking Station", "DOCK", 92m, 1.42m),
        new("Shipping Label Roll 4x6", "LABEL", 7m, 2.05m),
        new("Packing Tape 2 in", "TAPE", 2.2m, 2.45m),
        new("Barcode Scanner", "SCAN", 78m, 1.48m),
        new("Hand Truck 600 lb", "TRUCK", 96m, 1.38m),
        new("LED Shop Light 4 ft", "LIGHT", 24m, 1.72m),
        new("Steel Workbench 60 in", "BENCH", 155m, 1.34m),
        new("Safety Vest Class 2", "VEST", 5.8m, 2.10m),
        new("Warehouse Fan 24 in", "FAN", 84m, 1.44m),
        new("Cable Management Kit", "CMK", 6.4m, 2.00m),
        new("Rolling Ladder 5 Step", "LADDER", 182m, 1.32m),
        new("Cordless Drill Kit", "DRILL", 74m, 1.60m),
        new("Stainless Water Bottle 20 oz", "BOTTLE", 8.7m, 1.95m),
        new("Thermal Shipping Labels", "THERM", 16m, 1.80m),
        new("Office Chair Mesh Back", "CHAIR", 88m, 1.52m),
        new("Poly Mailer 10 x 13", "MAIL", 0.28m, 3.10m),
        new("Foldable Utility Cart", "CART", 112m, 1.40m),
        new("Desk Lamp LED Task", "LAMP", 18m, 1.88m),
        new("HDMI Cable 6 ft", "HDMI", 3.6m, 2.25m),
        new("Label Printer Desktop", "PRNT", 118m, 1.37m),
        new("Floor Mat Anti Fatigue", "MAT", 22m, 1.78m),
        new("Cable Organizer Bin", "ORGB", 11m, 1.92m),
        new("Rechargeable Flashlight", "FLASH", 21m, 1.86m),
        new("Portable Power Bank", "PWR", 19m, 1.88m),
        new("Document Shredder 12 Sheet", "SHRED", 64m, 1.58m)
    ];

    private static readonly string[] PurchaseNotes =
    [
        "Regional replenishment order",
        "Open purchase order receiving",
        "Seasonal stock build for store network",
        "Allocation for upcoming quarter demand",
        "Routine inbound replenishment"
    ];

    private static readonly string[] SalesNotes =
    [
        "Regional replenishment shipment",
        "Store set reset order",
        "Quarterly restock program",
        "Promotional inventory release",
        "Core assortment replenishment"
    ];

    private static readonly string[] TransferNotes =
    [
        "Rebalanced stock to support regional demand",
        "Moved inventory for weekend promotion coverage",
        "Shifted fast-moving units between facilities",
        "Allocated overflow stock to primary ship point"
    ];

    private static readonly string[] AdjustmentNotes =
    [
        "Cycle count variance confirmed during bin audit",
        "Receiving overage posted after carton recount",
        "Location clean-up adjustment after slotting review",
        "Pick-face variance cleared during warehouse audit"
    ];

    private static readonly string[] CustomerReturnNotes =
    [
        "Store return for shelf-damaged units",
        "Customer return tied to packaging issue",
        "Returned units from transit damage claim",
        "Merchandise returned after quality inspection"
    ];

    private static readonly string[] VendorReturnNotes =
    [
        "Supplier return for transit damage",
        "Return authorization issued for excess material",
        "Defective inbound units returned to vendor",
        "Packaging nonconformance return shipment"
    ];

    private static readonly string[] PaymentNotes =
    [
        "ACH remittance batch",
        "Scheduled payment release",
        "Wire settlement posted",
        "Lockbox receipt posted",
        "Net terms settlement"
    ];

    public async Task<TradeDemoSeedSummary> RunAsync(CancellationToken ct = default)
    {
        await EnsureTradeActivityDoesNotExistAsync(ct);

        var retainedEarningsAccountId = options.ClosePeriods
            ? await EnsureRetainedEarningsAccountAsync(ct)
            : Guid.Empty;

        var lookups = await LoadLookupsAsync(ct);
        var warehouses = await SeedWarehousesAsync(ct);
        var customers = await SeedPartiesAsync(isCustomer: true, isVendor: false, options.Customers, "C", lookups.Net30TermsId, ct);
        var vendors = await SeedPartiesAsync(isCustomer: false, isVendor: true, options.Vendors, "V", lookups.DueOnReceiptTermsId, ct);
        var items = await SeedItemsAsync(lookups.UnitOfMeasureId, lookups.RetailPriceTypeId, ct);

        var priceUpdatesPosted = await SeedPriceUpdatesAsync(items, lookups.RetailPriceTypeId, ct);
        var purchases = await SeedPurchaseReceiptsAsync(items, vendors, warehouses, ct);
        var sales = await SeedSalesInvoicesAsync(items, customers, warehouses, lookups.RetailPriceTypeId, ct);
        var transfersPosted = await SeedInventoryTransfersAsync(items, warehouses, ct);
        var adjustmentsPosted = await SeedInventoryAdjustmentsAsync(items, warehouses, lookups.CountCorrectionReasonId, ct);
        var customerReturnsPosted = await SeedCustomerReturnsAsync(sales, ct);
        var vendorReturnsPosted = await SeedVendorReturnsAsync(purchases, ct);
        var customerPaymentsPosted = await SeedCustomerPaymentsAsync(sales, ct);
        var vendorPaymentsPosted = await SeedVendorPaymentsAsync(purchases, ct);
        var closings = options.ClosePeriods
            ? await SeedPeriodClosingsAsync(retainedEarningsAccountId, ct)
            : new PeriodClosingSummary(0, 0);

        return new TradeDemoSeedSummary(
            options.FromDate,
            options.ToDate,
            warehouses.Count,
            customers.Count,
            vendors.Count,
            items.Count,
            priceUpdatesPosted,
            purchases.Count,
            sales.Count,
            customerPaymentsPosted,
            vendorPaymentsPosted,
            transfersPosted,
            adjustmentsPosted,
            customerReturnsPosted,
            vendorReturnsPosted,
            closings.MonthsClosed,
            closings.FiscalYearsClosed);
    }

    private async Task EnsureTradeActivityDoesNotExistAsync(CancellationToken ct)
    {
        foreach (var typeCode in TradeDocumentTypes)
        {
            var page = await documents.GetPageAsync(typeCode, new PageRequestDto(Offset: 0, Limit: 1, Search: null), ct);
            if (page.Total.GetValueOrDefault(page.Items.Count) > 0)
                throw new TradeSeedActivityAlreadyExistsException();
        }
    }

    private async Task<TradeSeedLookups> LoadLookupsAsync(CancellationToken ct)
        => new(
            UnitOfMeasureId: await GetCatalogIdByDisplayAsync(TradeCodes.UnitOfMeasure, "Each", ct),
            RetailPriceTypeId: await GetCatalogIdByDisplayAsync(TradeCodes.PriceType, "Retail", ct),
            Net30TermsId: await GetCatalogIdByDisplayAsync(TradeCodes.PaymentTerms, "Net 30", ct),
            DueOnReceiptTermsId: await GetCatalogIdByDisplayAsync(TradeCodes.PaymentTerms, "Due on Receipt", ct),
            CountCorrectionReasonId: await GetCatalogIdByDisplayAsync(TradeCodes.InventoryAdjustmentReason, "Count Correction", ct));

    private async Task<Guid> EnsureRetainedEarningsAccountAsync(CancellationToken ct)
    {
        const string retainedEarningsCode = "3200";
        const string retainedEarningsName = "Retained Earnings";

        var accounts = await chartOfAccountsAdmin.GetAsync(includeDeleted: true, ct);
        var existing = accounts.FirstOrDefault(x => string.Equals(x.Account.Code, retainedEarningsCode, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.IsDeleted)
                await chartOfAccountsManagement.UnmarkForDeletionAsync(existing.Account.Id, ct);

            if (!existing.IsActive)
                await chartOfAccountsManagement.SetActiveAsync(existing.Account.Id, true, ct);

            return existing.Account.Id;
        }

        return await chartOfAccountsManagement.CreateAsync(
            new CreateAccountRequest(
                Code: retainedEarningsCode,
                Name: retainedEarningsName,
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                NegativeBalancePolicy: null,
                IsActive: true),
            ct);
    }

    private async Task<List<WarehouseSeedResult>> SeedWarehousesAsync(CancellationToken ct)
    {
        var results = new List<WarehouseSeedResult>(options.Warehouses);

        for (var i = 0; i < options.Warehouses; i++)
        {
            var template = WarehouseTemplates[i % WarehouseTemplates.Length];
            var display = i < WarehouseTemplates.Length
                ? template.Name
                : $"{template.Name} {i / WarehouseTemplates.Length + 2}";
            var code = i < WarehouseTemplates.Length
                ? template.Code
                : $"{template.Code}{i / WarehouseTemplates.Length + 1}";

            var created = await catalogs.CreateAsync(
                TradeCodes.Warehouse,
                Payload(new
                {
                    display,
                    warehouse_code = code,
                    name = display,
                    address = template.Address,
                    is_active = true
                }),
                ct);

            results.Add(new WarehouseSeedResult(created.Id, display, code, template.Address));
        }

        return results;
    }

    private async Task<List<PartySeedResult>> SeedPartiesAsync(
        bool isCustomer,
        bool isVendor,
        int count,
        string numberPrefix,
        Guid paymentTermsId,
        CancellationToken ct)
    {
        var prefixes = isCustomer ? CustomerPrefixes : VendorPrefixes;
        var suffixes = isCustomer ? CustomerSuffixes : VendorSuffixes;
        var results = new List<PartySeedResult>(count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var display = BuildCompanyName(prefixes, suffixes, i, usedNames);
            var legalName = display.EndsWith("Group", StringComparison.OrdinalIgnoreCase)
                ? $"{display} LLC"
                : $"{display} Inc.";
            var city = PartyCities[i % PartyCities.Length];
            var streetNo = 100 + (i * 17);
            var streetName = StreetNames[i % StreetNames.Length];
            var address = $"{streetNo} {streetName} Ave, {city.City}, {city.State}";
            var phone = $"+1-{200 + (i % 500):000}-555-{1000 + (i % 9000):0000}";

            var created = await catalogs.CreateAsync(
                TradeCodes.Party,
                Payload(new
                {
                    display,
                    party_number = $"{numberPrefix}-{1000 + i}",
                    name = display,
                    legal_name = legalName,
                    phone,
                    billing_address = address,
                    shipping_address = address,
                    payment_terms_id = paymentTermsId,
                    default_currency = TradeCodes.DefaultCurrency,
                    is_customer = isCustomer,
                    is_vendor = isVendor,
                    is_active = true
                }),
                ct);

            results.Add(new PartySeedResult(created.Id, display));
        }

        return results;
    }

    private async Task<List<ItemSeedResult>> SeedItemsAsync(
        Guid unitOfMeasureId,
        Guid retailPriceTypeId,
        CancellationToken ct)
    {
        var results = new List<ItemSeedResult>(options.Items);

        for (var i = 0; i < options.Items; i++)
        {
            var template = ItemTemplates[i % ItemTemplates.Length];
            var series = i / ItemTemplates.Length;
            var display = series == 0
                ? template.Display
                : $"{template.Display} Mk {series + 2}";
            var sku = $"{template.SkuPrefix}-{1000 + i}";
            var baseCost = RoundMoney(template.BaseCost + (series * 1.75m));

            var created = await catalogs.CreateAsync(
                TradeCodes.Item,
                Payload(new
                {
                    display,
                    name = display,
                    sku,
                    unit_of_measure_id = unitOfMeasureId,
                    item_type = "Inventory",
                    is_inventory_item = true,
                    default_sales_price_type_id = retailPriceTypeId,
                    is_active = true
                }),
                ct);

            results.Add(new ItemSeedResult(created.Id, display, sku, baseCost, RoundMoney(baseCost * template.Markup)));
        }

        return results;
    }

    private async Task<int> SeedPriceUpdatesAsync(
        IReadOnlyList<ItemSeedResult> items,
        Guid retailPriceTypeId,
        CancellationToken ct)
    {
        var dates = BuildSpreadDates(options.PriceUpdates, options.FromDate, options.ToDate);
        var posted = 0;

        for (var i = 0; i < dates.Count; i++)
        {
            var date = dates[i];
            var selectedItems = i == 0
                ? items.ToList()
                : PickDistinctItems(items, Math.Min(items.Count, Math.Max(3, 4 + _random.Next(8))));

            var rows = new List<ItemPriceUpdateSeedLine>(selectedItems.Count);
            for (var lineNo = 0; lineNo < selectedItems.Count; lineNo++)
            {
                var item = selectedItems[lineNo];
                var newPrice = i == 0
                    ? item.InitialRetailPrice
                    : RoundMoney(_currentRetailPrices[item.Id] * RandomFactor(0.97m, 1.08m));

                _currentRetailPrices[item.Id] = newPrice;
                rows.Add(new ItemPriceUpdateSeedLine(lineNo + 1, item.Id, retailPriceTypeId, TradeCodes.DefaultCurrency, newPrice));
            }

            await CreateAndPostAsync(
                TradeCodes.ItemPriceUpdate,
                date,
                Payload(
                    new
                    {
                        effective_date = date.ToString("yyyy-MM-dd"),
                        notes = i == 0 ? "Opening retail price book" : MaybeNote(PurchaseNotes)
                    },
                    ItemPriceUpdateLines(rows)),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<List<PurchaseReceiptSeedResult>> SeedPurchaseReceiptsAsync(
        IReadOnlyList<ItemSeedResult> items,
        IReadOnlyList<PartySeedResult> vendors,
        IReadOnlyList<WarehouseSeedResult> warehouses,
        CancellationToken ct)
    {
        var receipts = new List<PurchaseReceiptSeedResult>(options.PurchaseReceipts);

        for (var i = 0; i < warehouses.Count; i++)
        {
            var warehouse = warehouses[i];
            var vendor = vendors[i % vendors.Count];
            var date = options.FromDate.AddDays(Math.Min(i * 2, Math.Max(0, options.ToDate.DayNumber - options.FromDate.DayNumber)));
            var rows = new List<PurchaseReceiptSeedLine>(items.Count);
            var total = 0m;

            for (var lineNo = 0; lineNo < items.Count; lineNo++)
            {
                var item = items[lineNo];
                var quantity = 80m + _random.Next(40, 121);
                var unitCost = RoundMoney(item.BaseCost * RandomFactor(0.96m, 1.04m));
                var lineAmount = RoundMoney(quantity * unitCost);
                rows.Add(new PurchaseReceiptSeedLine(lineNo + 1, item.Id, quantity, unitCost, lineAmount));
                AddInventory(warehouse.Id, item.Id, quantity, date);
                total += lineAmount;
            }

            var posted = await CreateAndPostAsync(
                TradeCodes.PurchaseReceipt,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        vendor_id = vendor.Id,
                        warehouse_id = warehouse.Id,
                        notes = "Opening warehouse stock receipt"
                    },
                    PurchaseReceiptLines(rows)),
                ct);

            receipts.Add(new PurchaseReceiptSeedResult(
                posted.Id,
                vendor.Id,
                warehouse.Id,
                date,
                total,
                rows.Select(x => new PurchaseReceiptLineState(x.ItemId, x.UnitCost, x.Quantity)).ToList()));
        }

        var remainingDates = BuildSpreadDates(options.PurchaseReceipts - warehouses.Count, options.FromDate, options.ToDate);
        foreach (var date in remainingDates)
        {
            var vendor = vendors[_random.Next(vendors.Count)];
            var warehouse = warehouses[_random.Next(warehouses.Count)];
            var lineCount = Math.Min(items.Count, 2 + _random.Next(5));
            var selectedItems = PickDistinctItems(items, lineCount);
            var rows = new List<PurchaseReceiptSeedLine>(selectedItems.Count);
            var total = 0m;

            for (var lineNo = 0; lineNo < selectedItems.Count; lineNo++)
            {
                var item = selectedItems[lineNo];
                var quantity = 12m + _random.Next(12, 96);
                var unitCost = RoundMoney(item.BaseCost * RandomFactor(0.92m, 1.08m));
                var lineAmount = RoundMoney(quantity * unitCost);
                rows.Add(new PurchaseReceiptSeedLine(lineNo + 1, item.Id, quantity, unitCost, lineAmount));
                AddInventory(warehouse.Id, item.Id, quantity, date);
                total += lineAmount;
            }

            var posted = await CreateAndPostAsync(
                TradeCodes.PurchaseReceipt,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        vendor_id = vendor.Id,
                        warehouse_id = warehouse.Id,
                        notes = MaybeNote(PurchaseNotes)
                    },
                    PurchaseReceiptLines(rows)),
                ct);

            receipts.Add(new PurchaseReceiptSeedResult(
                posted.Id,
                vendor.Id,
                warehouse.Id,
                date,
                total,
                rows.Select(x => new PurchaseReceiptLineState(x.ItemId, x.UnitCost, x.Quantity)).ToList()));
        }

        return receipts.OrderBy(x => x.Date).ToList();
    }

    private async Task<List<SalesInvoiceSeedResult>> SeedSalesInvoicesAsync(
        IReadOnlyList<ItemSeedResult> items,
        IReadOnlyList<PartySeedResult> customers,
        IReadOnlyList<WarehouseSeedResult> warehouses,
        Guid retailPriceTypeId,
        CancellationToken ct)
    {
        var invoices = new List<SalesInvoiceSeedResult>(options.SalesInvoices);
        var dates = BuildSpreadDates(options.SalesInvoices, options.FromDate, options.ToDate);

        foreach (var date in dates)
        {
            var customer = customers[_random.Next(customers.Count)];
            var warehouseOptions = warehouses
                .Select(warehouse => new
                {
                    Warehouse = warehouse,
                    Available = GetAvailableItems(warehouse.Id, items, date)
                })
                .Where(x => x.Available.Count > 0)
                .ToList();
            if (warehouseOptions.Count == 0)
                continue;

            var selectedWarehouse = warehouseOptions[_random.Next(warehouseOptions.Count)];
            var warehouse = selectedWarehouse.Warehouse;
            var available = selectedWarehouse.Available;

            var lineCount = Math.Min(available.Count, 1 + _random.Next(4));
            var selectedItems = PickDistinctItems(available, lineCount);
            var rows = new List<SalesInvoiceSeedLine>(selectedItems.Count);
            var total = 0m;

            for (var lineNo = 0; lineNo < selectedItems.Count; lineNo++)
            {
                var item = selectedItems[lineNo];
                var availableQty = GetInventory(warehouse.Id, item.Id, date);
                var quantity = Math.Min(availableQty, 2m + _random.Next(1, 12));
                if (quantity <= 0m)
                    continue;

                var unitCost = RoundMoney(item.BaseCost * RandomFactor(0.98m, 1.02m));
                var unitPrice = _currentRetailPrices.TryGetValue(item.Id, out var currentPrice)
                    ? currentPrice
                    : item.InitialRetailPrice;
                var lineAmount = RoundMoney(quantity * unitPrice);

                rows.Add(new SalesInvoiceSeedLine(lineNo + 1, item.Id, quantity, unitPrice, unitCost, lineAmount));
                RemoveInventory(warehouse.Id, item.Id, quantity, date);
                total += lineAmount;
            }

            if (rows.Count == 0)
                continue;

            var posted = await CreateAndPostAsync(
                TradeCodes.SalesInvoice,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        customer_id = customer.Id,
                        warehouse_id = warehouse.Id,
                        price_type_id = retailPriceTypeId,
                        notes = MaybeNote(SalesNotes)
                    },
                    SalesInvoiceLines(rows)),
                ct);

            invoices.Add(new SalesInvoiceSeedResult(
                posted.Id,
                customer.Id,
                warehouse.Id,
                date,
                total,
                rows.Select(x => new SalesInvoiceLineState(x.ItemId, x.UnitPrice, x.UnitCost, x.Quantity)).ToList()));
        }

        return invoices.OrderBy(x => x.Date).ToList();
    }

    private async Task<int> SeedInventoryTransfersAsync(
        IReadOnlyList<ItemSeedResult> items,
        IReadOnlyList<WarehouseSeedResult> warehouses,
        CancellationToken ct)
    {
        var posted = 0;
        var dates = BuildSpreadDates(options.InventoryTransfers, options.FromDate, options.ToDate);

        foreach (var date in dates)
        {
            var sourceOptions = warehouses
                .Select(warehouse => new
                {
                    Warehouse = warehouse,
                    Available = GetAvailableItems(warehouse.Id, items, date)
                })
                .Where(x => x.Available.Count > 0)
                .ToList();
            if (sourceOptions.Count == 0)
                continue;

            var selectedSource = sourceOptions[_random.Next(sourceOptions.Count)];
            var fromWarehouse = selectedSource.Warehouse;
            var available = selectedSource.Available;
            var destinationOptions = warehouses.Where(x => x.Id != fromWarehouse.Id).ToList();
            if (destinationOptions.Count == 0)
                continue;

            var toWarehouse = destinationOptions[_random.Next(destinationOptions.Count)];

            var lineCount = Math.Min(available.Count, 1 + _random.Next(3));
            var selectedItems = PickDistinctItems(available, lineCount);
            var rows = new List<InventoryTransferSeedLine>(selectedItems.Count);

            for (var lineNo = 0; lineNo < selectedItems.Count; lineNo++)
            {
                var item = selectedItems[lineNo];
                var availableQty = GetInventory(fromWarehouse.Id, item.Id, date);
                var quantity = Math.Min(availableQty, 1m + _random.Next(1, 10));
                if (quantity <= 0m)
                    continue;

                rows.Add(new InventoryTransferSeedLine(rows.Count + 1, item.Id, quantity));
                RemoveInventory(fromWarehouse.Id, item.Id, quantity, date);
                AddInventory(toWarehouse.Id, item.Id, quantity, date);
            }

            if (rows.Count == 0)
                continue;

            await CreateAndPostAsync(
                TradeCodes.InventoryTransfer,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        from_warehouse_id = fromWarehouse.Id,
                        to_warehouse_id = toWarehouse.Id,
                        notes = MaybeNote(TransferNotes)
                    },
                    InventoryTransferLines(rows)),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<int> SeedInventoryAdjustmentsAsync(
        IReadOnlyList<ItemSeedResult> items,
        IReadOnlyList<WarehouseSeedResult> warehouses,
        Guid countCorrectionReasonId,
        CancellationToken ct)
    {
        var posted = 0;
        var dates = BuildSpreadDates(options.InventoryAdjustments, options.FromDate, options.ToDate);

        foreach (var date in dates)
        {
            var warehouse = warehouses[_random.Next(warehouses.Count)];
            var lineCount = Math.Min(items.Count, 1 + _random.Next(2));
            var selectedItems = PickDistinctItems(items, lineCount);
            var rows = new List<InventoryAdjustmentSeedLine>(selectedItems.Count);

            for (var lineNo = 0; lineNo < selectedItems.Count; lineNo++)
            {
                var item = selectedItems[lineNo];
                var currentQty = GetInventory(warehouse.Id, item.Id, date);
                var makePositive = currentQty < 5m || _random.NextDouble() < 0.55d;
                decimal quantityDelta;

                if (makePositive)
                    quantityDelta = 1m + _random.Next(1, 6);
                else
                    quantityDelta = -Math.Min(currentQty, 1m + _random.Next(1, 4));

                if (quantityDelta == 0m)
                    quantityDelta = 1m;

                var unitCost = RoundMoney(item.BaseCost * RandomFactor(0.97m, 1.03m));
                var lineAmount = RoundMoney(Math.Abs(quantityDelta) * unitCost);
                rows.Add(new InventoryAdjustmentSeedLine(lineNo + 1, item.Id, quantityDelta, unitCost, lineAmount));

                if (quantityDelta > 0)
                    AddInventory(warehouse.Id, item.Id, quantityDelta, date);
                else
                    RemoveInventory(warehouse.Id, item.Id, Math.Abs(quantityDelta), date);
            }

            await CreateAndPostAsync(
                TradeCodes.InventoryAdjustment,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        warehouse_id = warehouse.Id,
                        reason_id = countCorrectionReasonId,
                        notes = MaybeNote(AdjustmentNotes)
                    },
                    InventoryAdjustmentLines(rows)),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<int> SeedCustomerReturnsAsync(IReadOnlyList<SalesInvoiceSeedResult> sales, CancellationToken ct)
    {
        var posted = 0;

        for (var i = 0; i < options.CustomerReturns; i++)
        {
            var datedSales = sales
                .Where(x => x.Date < options.ToDate)
                .ToList();
            var source = datedSales.Count == 0
                ? null
                : datedSales[_random.Next(datedSales.Count)];
            var date = source is null
                ? RandomDate(options.FromDate, options.ToDate)
                : RandomLaterDate(source.Date, options.ToDate);

            var rows = new List<CustomerReturnSeedLine>();
            Guid customerId;
            Guid warehouseId;
            Guid? sourceId = null;

            if (source is not null)
            {
                customerId = source.CustomerId;
                warehouseId = source.WarehouseId;
                sourceId = source.Id;

                var eligibleLines = source.Lines
                    .Where(x => x.RemainingQuantity > 0m)
                    .ToList();
                if (eligibleLines.Count == 0)
                    eligibleLines = source.Lines;

                var lineCount = Math.Min(eligibleLines.Count, 1 + _random.Next(2));
                var selectedLines = PickDistinctStates(eligibleLines, lineCount);

                for (var lineNo = 0; lineNo < selectedLines.Count; lineNo++)
                {
                    var line = selectedLines[lineNo];
                    var quantity = Math.Min(line.RemainingQuantity > 0m ? line.RemainingQuantity : 1m, 1m + _random.Next(1, 3));
                    quantity = Math.Max(1m, quantity);
                    line.RemainingQuantity -= quantity;
                    var lineAmount = RoundMoney(quantity * line.UnitPrice);
                    rows.Add(new CustomerReturnSeedLine(lineNo + 1, line.ItemId, quantity, line.UnitPrice, line.UnitCost, lineAmount));
                    AddInventory(warehouseId, line.ItemId, quantity, date);
                    source.OutstandingAmount = Math.Max(0m, source.OutstandingAmount - lineAmount);
                }
            }
            else
            {
                var fallback = sales[_random.Next(sales.Count)];
                customerId = fallback.CustomerId;
                warehouseId = fallback.WarehouseId;
                var line = fallback.Lines[_random.Next(fallback.Lines.Count)];
                var quantity = 1m;
                var lineAmount = RoundMoney(quantity * line.UnitPrice);
                rows.Add(new CustomerReturnSeedLine(1, line.ItemId, quantity, line.UnitPrice, line.UnitCost, lineAmount));
                AddInventory(warehouseId, line.ItemId, quantity, date);
            }

            await CreateAndPostAsync(
                TradeCodes.CustomerReturn,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        customer_id = customerId,
                        warehouse_id = warehouseId,
                        sales_invoice_id = sourceId,
                        notes = MaybeNote(CustomerReturnNotes)
                    },
                    CustomerReturnLines(rows)),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<int> SeedVendorReturnsAsync(
        IReadOnlyList<PurchaseReceiptSeedResult> receipts,
        CancellationToken ct)
    {
        var posted = 0;

        for (var i = 0; i < options.VendorReturns; i++)
        {
            var datedReceipts = receipts
                .Where(x => x.Date < options.ToDate)
                .ToList();
            var source = datedReceipts.Count == 0
                ? null
                : datedReceipts[_random.Next(datedReceipts.Count)];
            var date = source is null
                ? RandomDate(options.FromDate, options.ToDate)
                : RandomLaterDate(source.Date, options.ToDate);

            var rows = new List<VendorReturnSeedLine>();
            Guid vendorId;
            Guid warehouseId;
            Guid? sourceId = null;

            if (source is not null)
            {
                vendorId = source.VendorId;
                warehouseId = source.WarehouseId;
                sourceId = source.Id;

                var eligibleLines = source.Lines
                    .Where(x => x.RemainingQuantity > 0m && GetInventory(warehouseId, x.ItemId, date) > 0m)
                    .ToList();
                if (eligibleLines.Count == 0)
                    eligibleLines = source.Lines
                        .Where(x => GetInventory(warehouseId, x.ItemId, date) > 0m)
                        .ToList();

                if (eligibleLines.Count == 0)
                    continue;

                var lineCount = Math.Min(eligibleLines.Count, 1 + _random.Next(2));
                var selectedLines = PickDistinctStates(eligibleLines, lineCount);

                for (var lineNo = 0; lineNo < selectedLines.Count; lineNo++)
                {
                    var line = selectedLines[lineNo];
                    var onHand = GetInventory(warehouseId, line.ItemId, date);
                    var quantity = Math.Min(Math.Min(line.RemainingQuantity > 0m ? line.RemainingQuantity : 1m, onHand), 1m + _random.Next(1, 3));
                    if (quantity <= 0m)
                        continue;

                    line.RemainingQuantity -= quantity;
                    var lineAmount = RoundMoney(quantity * line.UnitCost);
                    rows.Add(new VendorReturnSeedLine(rows.Count + 1, line.ItemId, quantity, line.UnitCost, lineAmount));
                    RemoveInventory(warehouseId, line.ItemId, quantity, date);
                    source.OutstandingAmount = Math.Max(0m, source.OutstandingAmount - lineAmount);
                }
            }
            else
            {
                var fallback = receipts[_random.Next(receipts.Count)];
                vendorId = fallback.VendorId;
                warehouseId = fallback.WarehouseId;
                var line = fallback.Lines.FirstOrDefault(x => GetInventory(warehouseId, x.ItemId, date) > 0m);
                if (line is null)
                    continue;

                var quantity = 1m;
                var lineAmount = RoundMoney(quantity * line.UnitCost);
                rows.Add(new VendorReturnSeedLine(1, line.ItemId, quantity, line.UnitCost, lineAmount));
                RemoveInventory(warehouseId, line.ItemId, quantity, date);
            }

            if (rows.Count == 0)
                continue;

            await CreateAndPostAsync(
                TradeCodes.VendorReturn,
                date,
                Payload(
                    new
                    {
                        document_date_utc = date.ToString("yyyy-MM-dd"),
                        vendor_id = vendorId,
                        warehouse_id = warehouseId,
                        purchase_receipt_id = sourceId,
                        notes = MaybeNote(VendorReturnNotes)
                    },
                    VendorReturnLines(rows)),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<int> SeedCustomerPaymentsAsync(
        IReadOnlyList<SalesInvoiceSeedResult> sales,
        CancellationToken ct)
    {
        var posted = 0;

        for (var i = 0; i < options.CustomerPayments; i++)
        {
            var openInvoices = sales
                .Where(x => x.OutstandingAmount > 0.01m && x.Date < options.ToDate)
                .ToList();
            var source = openInvoices.Count == 0
                ? null
                : openInvoices[_random.Next(openInvoices.Count)];
            var date = source is null
                ? RandomDate(options.FromDate, options.ToDate)
                : RandomLaterDate(source.Date, options.ToDate);

            Guid customerId;
            Guid? salesInvoiceId = null;
            decimal amount;

            if (source is not null)
            {
                customerId = source.CustomerId;
                salesInvoiceId = source.Id;
                amount = RoundMoney(Math.Max(5m, source.OutstandingAmount * RandomFactor(0.25m, 0.60m)));
                amount = Math.Min(amount, source.OutstandingAmount);
                source.OutstandingAmount = Math.Max(0m, source.OutstandingAmount - amount);
            }
            else
            {
                var fallback = sales[_random.Next(sales.Count)];
                customerId = fallback.CustomerId;
                amount = RoundMoney(Math.Max(5m, fallback.TotalAmount * 0.20m));
            }

            await CreateAndPostAsync(
                TradeCodes.CustomerPayment,
                date,
                Payload(new
                {
                    document_date_utc = date.ToString("yyyy-MM-dd"),
                    customer_id = customerId,
                    sales_invoice_id = salesInvoiceId,
                    amount,
                    notes = MaybeNote(PaymentNotes)
                }),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<int> SeedVendorPaymentsAsync(
        IReadOnlyList<PurchaseReceiptSeedResult> receipts,
        CancellationToken ct)
    {
        var posted = 0;

        for (var i = 0; i < options.VendorPayments; i++)
        {
            var openReceipts = receipts
                .Where(x => x.OutstandingAmount > 0.01m && x.Date < options.ToDate)
                .ToList();
            var source = openReceipts.Count == 0
                ? null
                : openReceipts[_random.Next(openReceipts.Count)];
            var date = source is null
                ? RandomDate(options.FromDate, options.ToDate)
                : RandomLaterDate(source.Date, options.ToDate);

            Guid vendorId;
            Guid? purchaseReceiptId = null;
            decimal amount;

            if (source is not null)
            {
                vendorId = source.VendorId;
                purchaseReceiptId = source.Id;
                amount = RoundMoney(Math.Max(5m, source.OutstandingAmount * RandomFactor(0.25m, 0.60m)));
                amount = Math.Min(amount, source.OutstandingAmount);
                source.OutstandingAmount = Math.Max(0m, source.OutstandingAmount - amount);
            }
            else
            {
                var fallback = receipts[_random.Next(receipts.Count)];
                vendorId = fallback.VendorId;
                amount = RoundMoney(Math.Max(5m, fallback.TotalAmount * 0.20m));
            }

            await CreateAndPostAsync(
                TradeCodes.VendorPayment,
                date,
                Payload(new
                {
                    document_date_utc = date.ToString("yyyy-MM-dd"),
                    vendor_id = vendorId,
                    purchase_receipt_id = purchaseReceiptId,
                    amount,
                    notes = MaybeNote(PaymentNotes)
                }),
                ct);

            posted++;
        }

        return posted;
    }

    private async Task<PeriodClosingSummary> SeedPeriodClosingsAsync(
        Guid retainedEarningsAccountId,
        CancellationToken ct)
    {
        var currentYear = timeProvider.GetUtcNow().UtcDateTime.Year;
        var firstMonth = new DateOnly(options.FromDate.Year, options.FromDate.Month, 1);
        var lastMonth = new DateOnly(options.ToDate.Year, options.ToDate.Month, 1);
        var firstTrackedMonth = new DateOnly(firstMonth.Year, 1, 1);
        var closedPeriods = (await closedPeriodReader.GetClosedAsync(firstTrackedMonth, lastMonth, ct))
            .Select(x => x.Period)
            .ToHashSet();

        var monthsClosed = 0;
        var fiscalYearsClosed = 0;

        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            if (month.Year >= currentYear)
                break;

            if (month.Month == 12)
            {
                monthsClosed += await EnsureMonthsClosedAsync(
                    new DateOnly(month.Year, 1, 1),
                    month.AddMonths(-1),
                    closedPeriods,
                    ct);
                await periodClosing.CloseFiscalYearAsync(month, retainedEarningsAccountId, "System", ct);
                fiscalYearsClosed++;
            }

            monthsClosed += await EnsureMonthClosedAsync(month, closedPeriods, ct);
        }

        return new PeriodClosingSummary(monthsClosed, fiscalYearsClosed);
    }

    private async Task<int> EnsureMonthsClosedAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        HashSet<DateOnly> closedPeriods,
        CancellationToken ct)
    {
        var closedNow = 0;
        for (var month = fromInclusive; month <= toInclusive; month = month.AddMonths(1))
            closedNow += await EnsureMonthClosedAsync(month, closedPeriods, ct);

        return closedNow;
    }

    private async Task<int> EnsureMonthClosedAsync(
        DateOnly month,
        HashSet<DateOnly> closedPeriods,
        CancellationToken ct)
    {
        if (closedPeriods.Contains(month))
            return 0;

        await periodClosing.CloseMonthAsync(month, "System", ct);
        closedPeriods.Add(month);
        return 1;
    }

    private async Task<Guid> GetCatalogIdByDisplayAsync(string catalogType, string display, CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: display),
            ct);

        var matches = page.Items
            .Where(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0].Id,
            0 => throw new NgbConfigurationViolationException($"Default '{catalogType}' record '{display}' was not found."),
            _ => throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.")
        };
    }

    private async Task<DocumentDto> CreateAndPostAsync(
        string typeCode,
        DateOnly businessDate,
        RecordPayload payload,
        CancellationToken ct)
    {
        var created = await documents.CreateDraftAsync(typeCode, payload, ct);
        await drafts.UpdateDraftAsync(
            created.Id,
            number: null,
            dateUtc: DateTime.SpecifyKind(businessDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            manageTransaction: true,
            ct: ct);

        return await documents.PostAsync(typeCode, created.Id, ct);
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        return new RecordPayload(dict, parts);
    }

    private static IReadOnlyDictionary<string, RecordPartPayload> ItemPriceUpdateLines(
        IReadOnlyList<ItemPriceUpdateSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["price_type_id"] = JsonSerializer.SerializeToElement(row.PriceTypeId),
                ["currency"] = JsonSerializer.SerializeToElement(row.Currency),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> PurchaseReceiptLines(
        IReadOnlyList<PurchaseReceiptSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> SalesInvoiceLines(
        IReadOnlyList<SalesInvoiceSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> InventoryTransferLines(
        IReadOnlyList<InventoryTransferSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> InventoryAdjustmentLines(
        IReadOnlyList<InventoryAdjustmentSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity_delta"] = JsonSerializer.SerializeToElement(row.QuantityDelta),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> CustomerReturnLines(
        IReadOnlyList<CustomerReturnSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> VendorReturnLines(
        IReadOnlyList<VendorReturnSeedLine> rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> BuildRows<T>(
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyDictionary<string, JsonElement>> projector)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(projector(row));
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    private string BuildCompanyName(
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> suffixes,
        int index,
        ISet<string> used)
    {
        for (var attempt = 0; attempt < prefixes.Count * suffixes.Count * 2; attempt++)
        {
            var prefix = prefixes[(index + attempt) % prefixes.Count];
            var suffix = suffixes[((index * 3) + attempt) % suffixes.Count];
            var display = $"{prefix} {suffix}";
            if (used.Add(display))
                return display;
        }

        return $"{prefixes[index % prefixes.Count]} {suffixes[index % suffixes.Count]} {index + 1}";
    }

    private List<T> PickDistinctItems<T>(IReadOnlyList<T> source, int count)
    {
        if (count >= source.Count)
            return source.ToList();

        var indexes = Enumerable.Range(0, source.Count).OrderBy(_ => _random.Next()).Take(count);
        return indexes.Select(i => source[i]).ToList();
    }

    private List<T> PickDistinctStates<T>(IReadOnlyList<T> source, int count) where T : class
        => PickDistinctItems(source, count);

    private List<ItemSeedResult> GetAvailableItems(Guid warehouseId, IReadOnlyList<ItemSeedResult> allItems, DateOnly asOf)
        => allItems.Where(x => GetInventory(warehouseId, x.Id, asOf) > 0m).ToList();

    private decimal GetInventory(Guid warehouseId, Guid itemId, DateOnly asOf)
    {
        if (!_inventory.TryGetValue((warehouseId, itemId), out var timeline))
            return 0m;

        var total = 0m;
        foreach (var entry in timeline)
        {
            if (entry.Key > asOf)
                break;

            total += entry.Value;
        }

        return total;
    }

    private void AddInventory(Guid warehouseId, Guid itemId, decimal quantity, DateOnly asOf)
        => AddInventoryDelta(warehouseId, itemId, quantity, asOf);

    private void RemoveInventory(Guid warehouseId, Guid itemId, decimal quantity, DateOnly asOf)
        => AddInventoryDelta(warehouseId, itemId, -Math.Min(quantity, GetInventory(warehouseId, itemId, asOf)), asOf);

    private void AddInventoryDelta(Guid warehouseId, Guid itemId, decimal delta, DateOnly asOf)
    {
        if (delta == 0m)
            return;

        var key = (warehouseId, itemId);
        if (!_inventory.TryGetValue(key, out var timeline))
        {
            timeline = new SortedDictionary<DateOnly, decimal>();
            _inventory[key] = timeline;
        }

        if (timeline.TryGetValue(asOf, out var existing))
            timeline[asOf] = existing + delta;
        else
            timeline[asOf] = delta;

        if (timeline[asOf] == 0m)
            timeline.Remove(asOf);
    }

    private List<DateOnly> BuildSpreadDates(int count, DateOnly fromInclusive, DateOnly toInclusive)
    {
        var list = new List<DateOnly>(count);
        if (count <= 0)
            return list;

        var totalDays = Math.Max(0, toInclusive.DayNumber - fromInclusive.DayNumber);
        if (count == 1 || totalDays == 0)
        {
            for (var i = 0; i < count; i++)
            {
                list.Add(fromInclusive);
            }

            return list;
        }

        var spacing = Math.Max(1, totalDays / count);
        for (var i = 0; i < count; i++)
        {
            var anchor = (int)Math.Round((double)i * totalDays / (count - 1));
            var jitter = spacing <= 1 ? 0 : _random.Next(-spacing / 3, spacing / 3 + 1);
            var offset = Math.Clamp(anchor + jitter, 0, totalDays);
            list.Add(fromInclusive.AddDays(offset));
        }

        list.Sort();
        return list;
    }

    private DateOnly RandomDate(DateOnly fromInclusive, DateOnly toInclusive)
    {
        var range = Math.Max(0, toInclusive.DayNumber - fromInclusive.DayNumber);
        return fromInclusive.AddDays(range == 0 ? 0 : _random.Next(range + 1));
    }

    private DateOnly RandomLaterDate(DateOnly afterInclusive, DateOnly toInclusive)
    {
        var start = afterInclusive < toInclusive ? afterInclusive.AddDays(1) : afterInclusive;
        if (start > toInclusive)
            start = toInclusive;

        return RandomDate(start, toInclusive);
    }

    private decimal RandomFactor(decimal minInclusive, decimal maxInclusive)
    {
        var ratio = (decimal)_random.NextDouble();
        return minInclusive + ((maxInclusive - minInclusive) * ratio);
    }

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private string? MaybeNote(IReadOnlyList<string> variants)
        => _random.NextDouble() < 0.45d
            ? variants[_random.Next(variants.Count)]
            : null;

    private List<T> Shuffle<T>(IReadOnlyList<T> source)
        => source.OrderBy(_ => _random.Next()).ToList();
    private sealed record TradeSeedLookups(
        Guid UnitOfMeasureId,
        Guid RetailPriceTypeId,
        Guid Net30TermsId,
        Guid DueOnReceiptTermsId,
        Guid CountCorrectionReasonId);

    private sealed record WarehouseTemplate(string Code, string Name, string Address);

    private sealed record ItemTemplate(string Display, string SkuPrefix, decimal BaseCost, decimal Markup);

    private sealed record WarehouseSeedResult(Guid Id, string Display, string Code, string Address);

    private sealed record PartySeedResult(Guid Id, string Display);

    private sealed record ItemSeedResult(
        Guid Id,
        string Display,
        string Sku,
        decimal BaseCost,
        decimal InitialRetailPrice);

    private sealed record ItemPriceUpdateSeedLine(
        int Ordinal,
        Guid ItemId,
        Guid PriceTypeId,
        string Currency,
        decimal UnitPrice);

    private sealed record PurchaseReceiptSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);

    private sealed record SalesInvoiceSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    private sealed record InventoryTransferSeedLine(int Ordinal, Guid ItemId, decimal Quantity);

    private sealed record InventoryAdjustmentSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal QuantityDelta,
        decimal UnitCost,
        decimal LineAmount);

    private sealed record CustomerReturnSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    private sealed record VendorReturnSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);

    private sealed record PurchaseReceiptLineState(Guid ItemId, decimal UnitCost, decimal OriginalQuantity)
    {
        public decimal RemainingQuantity { get; set; } = OriginalQuantity;
    }

    private sealed record SalesInvoiceLineState(
        Guid ItemId,
        decimal UnitPrice,
        decimal UnitCost,
        decimal OriginalQuantity)
    {
        public decimal RemainingQuantity { get; set; } = OriginalQuantity;
    }

    private sealed record PurchaseReceiptSeedResult(
        Guid Id,
        Guid VendorId,
        Guid WarehouseId,
        DateOnly Date,
        decimal TotalAmount,
        List<PurchaseReceiptLineState> Lines)
    {
        public decimal OutstandingAmount { get; set; } = TotalAmount;
    }

    private sealed record SalesInvoiceSeedResult(
        Guid Id,
        Guid CustomerId,
        Guid WarehouseId,
        DateOnly Date,
        decimal TotalAmount,
        List<SalesInvoiceLineState> Lines)
    {
        public decimal OutstandingAmount { get; set; } = TotalAmount;
    }

    private sealed record PeriodClosingSummary(int MonthsClosed, int FiscalYearsClosed);

    private static readonly string[] TradeDocumentTypes =
    [
        TradeCodes.ItemPriceUpdate,
        TradeCodes.PurchaseReceipt,
        TradeCodes.SalesInvoice,
        TradeCodes.CustomerPayment,
        TradeCodes.VendorPayment,
        TradeCodes.InventoryTransfer,
        TradeCodes.InventoryAdjustment,
        TradeCodes.CustomerReturn,
        TradeCodes.VendorReturn
    ];
}
