using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Persistence.Readers.Periods;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Contracts.Catalogs;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Runtime;
using NGB.PropertyManagement.Runtime.Catalogs;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.Runtime.Accounts;
using NGB.Runtime.Periods;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Migrator.Seed;

internal static class PropertyManagementSeedDemoCli
{
    private const string CommandName = "seed-demo";

    public static bool IsSeedDemoCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args, TimeProvider? timeProvider = null)
    {
        PropertyManagementDemoSeedOptions? options = null;

        try
        {
            options = PropertyManagementDemoSeedOptions.Parse(args);
            var effectiveTimeProvider = timeProvider ?? TimeProvider.System;

            var services = new ServiceCollection();
            services.AddLogging();

            services
                .AddNgbRuntime()
                .AddNgbPostgres(options.ConnectionString)
                .AddPropertyManagementModule()
                .AddPropertyManagementRuntimeModule()
                .AddPropertyManagementPostgresModule();
            services.AddSingleton(effectiveTimeProvider);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await using (var setupScope = provider.CreateAsyncScope())
            {
                var setupService = setupScope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
                await setupService.EnsureDefaultsAsync();
            }

            await using var seedScope = provider.CreateAsyncScope();
            var seeder = new PropertyManagementDemoSeeder(
                options,
                seedScope.ServiceProvider.GetRequiredService<ICatalogService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentDraftService>(),
                seedScope.ServiceProvider.GetRequiredService<IPropertyBulkCreateUnitsService>(),
                seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>(),
                seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>(),
                seedScope.ServiceProvider.GetRequiredService<IPeriodClosingService>(),
                seedScope.ServiceProvider.GetRequiredService<IClosedPeriodReader>(),
                effectiveTimeProvider);

            var summary = await seeder.RunAsync();
            PrintSummary(summary);
            return 0;
        }
        catch (PropertyManagementDemoDatasetAlreadyExistsException) when (options?.SkipIfDatasetExists == true)
        {
            Console.WriteLine("OK: property management demo seed skipped because the dataset already exists.");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: property management demo seed error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintSummary(PropertyManagementDemoSeedSummary summary)
    {
        Console.WriteLine("OK: property management demo data seeded.");
        Console.WriteLine($"- Dataset: {summary.DatasetCode}");
        Console.WriteLine($"- Period: {summary.FromDate:yyyy-MM-dd} .. {summary.ToDate:yyyy-MM-dd}");
        Console.WriteLine($"- Buildings created: {summary.BuildingsCreated}");
        Console.WriteLine($"- Units created: {summary.UnitsCreated}");
        Console.WriteLine($"- Tenant parties created: {summary.TenantsCreated}");
        Console.WriteLine($"- Vendor parties created: {summary.VendorsCreated}");
        Console.WriteLine($"- Bank accounts available: {summary.BankAccountsAvailable}");
        Console.WriteLine($"- Lease documents posted: {summary.LeasesPosted}");
        Console.WriteLine($"- Rent Charge documents posted: {summary.RentChargesPosted}");
        Console.WriteLine($"- Receivable Charge documents posted: {summary.ReceivableChargesPosted}");
        Console.WriteLine($"- Late Fee Charge documents posted: {summary.LateFeeChargesPosted}");
        Console.WriteLine($"- Receivable Credit Memo documents posted: {summary.ReceivableCreditMemosPosted}");
        Console.WriteLine($"- Receivable Payment documents posted: {summary.ReceivablePaymentsPosted}");
        Console.WriteLine($"- Receivable Returned Payment documents posted: {summary.ReceivableReturnedPaymentsPosted}");
        Console.WriteLine($"- Receivable Apply documents posted: {summary.ReceivableAppliesPosted}");
        Console.WriteLine($"- Months closed: {summary.MonthsClosed}");
        Console.WriteLine($"- Fiscal years closed: {summary.FiscalYearsClosed}");
        Console.WriteLine($"- Payable Charge documents posted: {summary.PayableChargesPosted}");
        Console.WriteLine($"- Payable Credit Memo documents posted: {summary.PayableCreditMemosPosted}");
        Console.WriteLine($"- Payable Payment documents posted: {summary.PayablePaymentsPosted}");
        Console.WriteLine($"- Payable Apply documents posted: {summary.PayableAppliesPosted}");
        Console.WriteLine($"- Maintenance Request documents posted: {summary.MaintenanceRequestsPosted}");
        Console.WriteLine($"- Work Order documents posted: {summary.WorkOrdersPosted}");
        Console.WriteLine($"- Work Order Completion documents posted: {summary.WorkOrderCompletionsPosted}");
    }
}

internal sealed class PropertyManagementDemoDatasetAlreadyExistsException(string datasetCode)
    : NgbConflictException(
        message: $"Demo dataset '{datasetCode}' already exists.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["dataset"] = datasetCode
        })
{
    public const string ErrorCodeConst = "pm.seed_demo.dataset_already_exists";
}

internal sealed record PropertyManagementDemoSeedOptions(
    string ConnectionString,
    string DatasetCode,
    int Seed,
    DateOnly FromDate,
    DateOnly ToDate,
    int Buildings,
    int UnitsPerBuildingMin,
    int UnitsPerBuildingMax,
    int Tenants,
    int Vendors,
    double OccupancyRate,
    bool SkipIfDatasetExists)
{
    public static PropertyManagementDemoSeedOptions Parse(string[] args)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var connectionString = PropertyManagementSeedCliArgs.RequireConnectionString(args);
        var datasetCode = PropertyManagementSeedCliArgs.GetString(args, "--dataset", "demo");
        var seed = PropertyManagementSeedCliArgs.GetInt(args, "--seed", 20260327);
        var fromDate = PropertyManagementSeedCliArgs.GetDateOnly(args, "--from", new DateOnly(2024, 1, 1));
        var toDate = PropertyManagementSeedCliArgs.GetDateOnly(args, "--to", now);
        var buildings = PropertyManagementSeedCliArgs.GetInt(args, "--buildings", 6);
        var unitsMin = PropertyManagementSeedCliArgs.GetInt(args, "--units-min", 14);
        var unitsMax = PropertyManagementSeedCliArgs.GetInt(args, "--units-max", 20);
        var tenants = PropertyManagementSeedCliArgs.GetInt(args, "--tenants", 150);
        var vendors = PropertyManagementSeedCliArgs.GetInt(args, "--vendors", 28);
        var occupancyRate = PropertyManagementSeedCliArgs.GetDouble(args, "--occupancy-rate", 0.74d);
        var skipIfDatasetExists = PropertyManagementSeedCliArgs.GetBool(args, "--skip-if-dataset-exists", false);

        if (string.IsNullOrWhiteSpace(datasetCode))
            throw new NgbArgumentInvalidException("--dataset", "'--dataset' must be non-empty.");

        if (fromDate > toDate)
            throw new NgbArgumentInvalidException("--from", "'--from' must be less than or equal to '--to'.");

        if (buildings is <= 0 or > 10)
            throw new NgbArgumentOutOfRangeException("--buildings", buildings, "'--buildings' must be between 1 and 10.");

        if (unitsMin is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException("--units-min", unitsMin, "'--units-min' must be between 1 and 500.");

        if (unitsMax < unitsMin || unitsMax > 500)
            throw new NgbArgumentOutOfRangeException("--units-max", unitsMax, "'--units-max' must be between '--units-min' and 500.");

        if (tenants is <= 0 or > 1000)
            throw new NgbArgumentOutOfRangeException("--tenants", tenants, "'--tenants' must be between 1 and 1000.");

        if (vendors is <= 0 or > 200)
            throw new NgbArgumentOutOfRangeException("--vendors", vendors, "'--vendors' must be between 1 and 200.");

        if (occupancyRate is <= 0d or > 1d)
            throw new NgbArgumentOutOfRangeException("--occupancy-rate", occupancyRate, "'--occupancy-rate' must be > 0 and <= 1.");

        return new PropertyManagementDemoSeedOptions(
            connectionString,
            datasetCode.Trim(),
            seed,
            fromDate,
            toDate,
            buildings,
            unitsMin,
            unitsMax,
            tenants,
            vendors,
            occupancyRate,
            skipIfDatasetExists);
    }
}

internal sealed record PropertyManagementDemoSeedSummary(
    string DatasetCode,
    DateOnly FromDate,
    DateOnly ToDate,
    int BuildingsCreated,
    int UnitsCreated,
    int TenantsCreated,
    int VendorsCreated,
    int BankAccountsAvailable,
    int LeasesPosted,
    int RentChargesPosted,
    int ReceivableChargesPosted,
    int LateFeeChargesPosted,
    int ReceivableCreditMemosPosted,
    int ReceivablePaymentsPosted,
    int ReceivableReturnedPaymentsPosted,
    int ReceivableAppliesPosted,
    int MonthsClosed,
    int FiscalYearsClosed,
    int PayableChargesPosted,
    int PayableCreditMemosPosted,
    int PayablePaymentsPosted,
    int PayableAppliesPosted,
    int MaintenanceRequestsPosted,
    int WorkOrdersPosted,
    int WorkOrderCompletionsPosted);

internal sealed class PropertyManagementDemoSeeder(
    PropertyManagementDemoSeedOptions options,
    ICatalogService catalogs,
    IDocumentService documents,
    IDocumentDraftService drafts,
    IPropertyBulkCreateUnitsService bulkUnits,
    IChartOfAccountsAdminService chartOfAccountsAdmin,
    IChartOfAccountsManagementService chartOfAccountsManagement,
    IPeriodClosingService periodClosing,
    IClosedPeriodReader closedPeriodReader,
    TimeProvider timeProvider)
{
    private readonly Random _random = new(options.Seed);
    private readonly HashSet<string> _usedPartyDisplays = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedPartyEmails = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] StreetNames =
    [
        "Hudson", "River", "Garden", "Liberty", "Park", "Grand", "Willow", "Bloomfield", "Washington", "Observer"
    ];

    private static readonly (string City, string State, string Zip)[] NjCities =
    [
        ("Hoboken", "NJ", "07030"),
        ("Jersey City", "NJ", "07302"),
        ("Newark", "NJ", "07102"),
        ("Union City", "NJ", "07087"),
        ("Weehawken", "NJ", "07086")
    ];

    private static readonly string[] TenantFirstNames =
    [
        "Ava", "Benjamin", "Chloe", "Daniel", "Ella", "Finn", "Grace", "Henry", "Ivy", "Jack",
        "Kylie", "Landon", "Maya", "Noah", "Olivia", "Parker", "Quinn", "Ruby", "Samuel", "Taylor",
        "Uma", "Violet", "Wyatt", "Xavier", "Yara", "Zane"
    ];

    private static readonly string[] TenantLastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Martinez", "Anderson",
        "Taylor", "Thomas", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson", "White", "Harris",
        "Clark", "Lewis", "Walker", "Hall", "Allen", "Young", "King", "Wright", "Scott", "Torres",
        "Nguyen", "Hill", "Flores", "Green", "Adams", "Nelson", "Baker", "Rivera", "Campbell", "Mitchell",
        "Carter", "Roberts", "Phillips", "Evans", "Turner", "Parker", "Collins", "Edwards", "Stewart", "Morris"
    ];

    private static readonly string[] VendorPrefixes =
    [
        "Atlantic", "Beacon", "Crestview", "Delta", "Evergreen", "Firstline", "Grandview", "Harbor", "Ironwood", "Jetstream",
        "Keystone", "Landmark", "Meridian", "Northstar", "Oakridge", "Pinnacle", "Quayside", "Riverstone", "Silverline", "Tidewater"
    ];

    private static readonly string[] VendorServices =
    [
        "Appliance", "Builders", "Cleaning", "Electrical", "Flooring", "Heating", "Landscaping", "Painting", "Plumbing", "Restoration",
        "Roofing", "Security", "Utility", "Waterproofing", "Windows"
    ];

    private static readonly DemoBankAccountSeed[] DemoBankAccounts =
    [
        new("1010", "Operating Cash - Harbor State", "Harbor State Operating", "Harbor State Bank", "4101"),
        new("1020", "Operating Cash - Garden Federal", "Garden Federal Reserve", "Garden Federal", "4102"),
        new("1030", "Security Deposit Cash - Metro Credit", "Metro Credit Deposits", "Metro Credit Union", "4103")
    ];

    private static readonly string[] MaintenanceSubjects =
    [
        "Water leak in kitchen",
        "Heating not working",
        "Broken window latch",
        "Bathroom fan noise",
        "Appliance failure",
        "Hallway light out",
        "Door lock issue",
        "A/C not cooling"
    ];

    public async Task<PropertyManagementDemoSeedSummary> RunAsync(CancellationToken ct = default)
    {
        await EnsureDatasetDoesNotExistAsync(ct);
        var retainedEarningsAccountId = await EnsureRetainedEarningsAccountAsync(ct);

        await using var conn = new NpgsqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("SET TIME ZONE 'UTC';", cancellationToken: ct));

        await EnsureDemoBankAccountsAsync(ct);
        var lookup = await LoadLookupsAsync(conn, ct);
        await PrimeExistingPartyIdentitiesAsync(conn, ct);
        var buildings = await SeedBuildingsAndUnitsAsync(ct);
        var tenants = await SeedTenantsAsync(ct);
        var vendors = await SeedVendorsAsync(ct);

        var leasePlan = BuildLeasePlans(buildings.SelectMany(x => x.UnitIds).ToList(), tenants.Select(x => x.Id).ToList());
        var seededLeases = await SeedLeasesAsync(leasePlan, ct);

        var receivables = await SeedReceivablesAsync(seededLeases, lookup, ct);
        var payables = await SeedPayablesAsync(buildings, vendors, lookup, ct);
        var maintenance = await SeedMaintenanceAsync(buildings, tenants, vendors, lookup, ct);
        var closings = await SeedPeriodClosingsAsync(retainedEarningsAccountId, ct);

        return new PropertyManagementDemoSeedSummary(
            options.DatasetCode,
            options.FromDate,
            options.ToDate,
            buildings.Count,
            buildings.Sum(x => x.UnitIds.Count),
            tenants.Count,
            vendors.Count,
            lookup.BankAccounts.Count,
            seededLeases.Count,
            receivables.RentChargesPosted,
            receivables.ReceivableChargesPosted,
            receivables.LateFeeChargesPosted,
            receivables.ReceivableCreditMemosPosted,
            receivables.ReceivablePaymentsPosted,
            receivables.ReceivableReturnedPaymentsPosted,
            receivables.ReceivableAppliesPosted,
            closings.MonthsClosed,
            closings.FiscalYearsClosed,
            payables.PayableChargesPosted,
            payables.PayableCreditMemosPosted,
            payables.PayablePaymentsPosted,
            payables.PayableAppliesPosted,
            maintenance.RequestsPosted,
            maintenance.WorkOrdersPosted,
            maintenance.CompletionsPosted);
    }

    private async Task EnsureDatasetDoesNotExistAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct);

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                  from cat_pm_property
                 where kind = 'Building'
                   and address_line2 = @Marker
            );
            """,
            new { Marker = DatasetMarker() },
            cancellationToken: ct));

        if (exists)
            throw new PropertyManagementDemoDatasetAlreadyExistsException(options.DatasetCode);
    }

    private async Task<DemoLookup> LoadLookupsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var bankAccounts = (await conn.QueryAsync<LookupRow>(new CommandDefinition(
            "select catalog_id as Id, display as Name from cat_pm_bank_account order by is_default desc, display;",
            cancellationToken: ct))).ToList();

        var defaultBankAccountId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select catalog_id from cat_pm_bank_account where is_default = true order by catalog_id limit 1;",
            cancellationToken: ct));

        if (defaultBankAccountId is null || defaultBankAccountId == Guid.Empty)
            throw new NgbConfigurationViolationException("Default PM bank account was not found. Run seed-defaults first.");

        var receivableChargeTypes = (await conn.QueryAsync<LookupRow>(new CommandDefinition(
            "select catalog_id as Id, display as Name from cat_pm_receivable_charge_type order by display;",
            cancellationToken: ct))).ToList();

        var payableChargeTypes = (await conn.QueryAsync<LookupRow>(new CommandDefinition(
            "select catalog_id as Id, display as Name from cat_pm_payable_charge_type order by display;",
            cancellationToken: ct))).ToList();

        var maintenanceCategories = (await conn.QueryAsync<LookupRow>(new CommandDefinition(
            "select catalog_id as Id, display as Name from cat_pm_maintenance_category order by display;",
            cancellationToken: ct))).ToList();

        return new DemoLookup(
            defaultBankAccountId.Value,
            bankAccounts,
            receivableChargeTypes.Single(x => string.Equals(x.Name, "Utility", StringComparison.OrdinalIgnoreCase)).Id,
            receivableChargeTypes.Single(x => string.Equals(x.Name, "Parking", StringComparison.OrdinalIgnoreCase)).Id,
            payableChargeTypes.Single(x => string.Equals(x.Name, "Repair", StringComparison.OrdinalIgnoreCase)).Id,
            payableChargeTypes.Single(x => string.Equals(x.Name, "Utility", StringComparison.OrdinalIgnoreCase)).Id,
            maintenanceCategories);
    }

    private async Task EnsureDemoBankAccountsAsync(CancellationToken ct)
    {
        var activeBankAccounts = await catalogs.GetPageAsync(
            PropertyManagementCodes.BankAccount,
            new PageRequestDto(0, 200, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deleted"] = "active"
            }),
            ct);

        var existingByDisplay = activeBankAccounts.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Display))
            .GroupBy(x => x.Display!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var seed in DemoBankAccounts)
        {
            var glAccountId = await EnsureActiveCashEquivalentAssetAccountAsync(seed.AccountCode, seed.AccountName, ct);
            var payload = Payload(new
            {
                display = seed.Display,
                bank_name = seed.BankName,
                account_name = seed.AccountName,
                last4 = seed.Last4,
                gl_account_id = glAccountId,
                is_default = false
            });

            if (existingByDisplay.TryGetValue(seed.Display, out var existing))
            {
                await catalogs.UpdateAsync(PropertyManagementCodes.BankAccount, existing.Id, payload, ct);
                continue;
            }

            var created = await catalogs.CreateAsync(PropertyManagementCodes.BankAccount, payload, ct);
            existingByDisplay[seed.Display] = created;
        }
    }

    private async Task PrimeExistingPartyIdentitiesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<(string? Display, string? Email)>(new CommandDefinition(
            """
            select display as Display,
                   email as Email
              from cat_pm_party
             order by display nulls last, email nulls last;
            """,
            cancellationToken: ct));

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Display))
                _usedPartyDisplays.Add(row.Display.Trim());

            if (!string.IsNullOrWhiteSpace(row.Email))
                _usedPartyEmails.Add(row.Email.Trim());
        }
    }

    private async Task<Guid> EnsureActiveCashEquivalentAssetAccountAsync(string code, string name, CancellationToken ct)
    {
        var accounts = await chartOfAccountsAdmin.GetAsync(includeDeleted: true, ct);
        var existing = accounts.FirstOrDefault(x => string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.IsDeleted)
                await chartOfAccountsManagement.UnmarkForDeletionAsync(existing.Account.Id, ct);

            if (!existing.IsActive)
                await chartOfAccountsManagement.SetActiveAsync(existing.Account.Id, true, ct);

            if (existing.Account.Type != AccountType.Asset)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected type. Expected '{AccountType.Asset}', actual '{existing.Account.Type}'.");

            if (existing.Account.StatementSection != StatementSection.Assets)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected statement section. Expected '{StatementSection.Assets}', actual '{existing.Account.StatementSection}'.");

            if (existing.Account.CashFlowRole != CashFlowRole.CashEquivalent || !string.IsNullOrWhiteSpace(existing.Account.CashFlowLineCode))
            {
                await chartOfAccountsManagement.UpdateAsync(
                    new UpdateAccountRequest(
                        AccountId: existing.Account.Id,
                        CashFlowRole: CashFlowRole.CashEquivalent,
                        CashFlowLineCode: string.Empty),
                    ct);
            }

            return existing.Account.Id;
        }

        return await chartOfAccountsManagement.CreateAsync(
            new CreateAccountRequest(
                Code: code,
                Name: name,
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: null,
                IsActive: true,
                CashFlowRole: CashFlowRole.CashEquivalent),
            ct);
    }

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

    private async Task<List<BuildingSeedResult>> SeedBuildingsAndUnitsAsync(CancellationToken ct)
    {
        var results = new List<BuildingSeedResult>(options.Buildings);

        for (var i = 0; i < options.Buildings; i++)
        {
            var unitCount = _random.Next(options.UnitsPerBuildingMin, options.UnitsPerBuildingMax + 1);
            var city = NjCities[i % NjCities.Length];
            var streetNo = 100 + i * 17;
            var streetName = StreetNames[i % StreetNames.Length];

            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = $"{streetNo} {streetName} Ave",
                address_line2 = DatasetMarker(),
                city = city.City,
                state = city.State,
                zip = city.Zip
            }), ct);

            var bulk = await bulkUnits.BulkCreateUnitsAsync(new PropertyBulkCreateUnitsRequest
            {
                BuildingId = building.Id,
                FromInclusive = 101,
                ToInclusive = 100 + unitCount,
                Step = 1,
                UnitNoFormat = "{0:000}",
                FloorSize = 100
            }, ct);

            results.Add(new BuildingSeedResult(building.Id, bulk.CreatedIds.ToList()));
        }

        return results;
    }

    private async Task<List<PartySeedResult>> SeedTenantsAsync(CancellationToken ct)
    {
        var identities = AllocatePartyIdentities(BuildTenantBaseDisplays(), options.Tenants, "tenant");
        var list = new List<PartySeedResult>(options.Tenants);
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = identity.Display,
                email = identity.Email,
                phone = DemoPhone(i),
                is_tenant = true,
                is_vendor = false
            }), ct);

            list.Add(new PartySeedResult(party.Id, identity.Display));
        }

        return list;
    }

    private async Task<List<PartySeedResult>> SeedVendorsAsync(CancellationToken ct)
    {
        var identities = AllocatePartyIdentities(BuildVendorBaseDisplays(), options.Vendors, "vendor");
        var list = new List<PartySeedResult>(options.Vendors);
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
            {
                display = identity.Display,
                email = identity.Email,
                phone = DemoPhone(i + 10_000),
                is_tenant = false,
                is_vendor = true
            }), ct);

            list.Add(new PartySeedResult(party.Id, identity.Display));
        }

        return list;
    }

    private List<LeasePlan> BuildLeasePlans(IReadOnlyList<Guid> unitIds, IReadOnlyList<Guid> tenantIds)
    {
        var shuffledUnits = unitIds.OrderBy(_ => _random.Next()).ToList();
        var shuffledTenants = tenantIds.OrderBy(_ => _random.Next()).ToList();

        var occupiedUnitCount = Math.Max(1, Math.Min(shuffledUnits.Count, (int)Math.Round(shuffledUnits.Count * options.OccupancyRate, MidpointRounding.AwayFromZero)));
        occupiedUnitCount = Math.Min(occupiedUnitCount, shuffledTenants.Count);

        var plans = new List<LeasePlan>();
        var tenantIndex = 0;
        var rangeDays = options.ToDate.DayNumber - options.FromDate.DayNumber;
        var canCreateClosedLease = rangeDays >= 45;
        var canCreateTurnover = rangeDays >= 120;

        for (var i = 0; i < occupiedUnitCount && tenantIndex < shuffledTenants.Count; i++)
        {
            var unitId = shuffledUnits[i];
            var modeRoll = _random.NextDouble();

            if (canCreateTurnover && modeRoll < 0.15d && tenantIndex + 1 < shuffledTenants.Count)
            {
                var predecessorStart = RandomDate(options.FromDate, options.ToDate.AddDays(-120));
                var predecessorEnd = ClampDate(
                    predecessorStart.AddMonths(_random.Next(3, 10)).AddDays(-1),
                    predecessorStart.AddDays(30),
                    options.ToDate.AddDays(-45));

                if (predecessorEnd > predecessorStart)
                {
                    plans.Add(new LeasePlan(
                        unitId, 
                        shuffledTenants[tenantIndex++],
                        predecessorStart,
                        predecessorEnd,
                        RentAmount(),
                        DueDay(),
                        false));

                    var activeStart = ClampDate(
                        predecessorEnd.AddDays(_random.Next(15, 61)),
                        predecessorEnd.AddDays(1),
                        options.ToDate.AddDays(-14));

                    if (activeStart > predecessorEnd && activeStart <= options.ToDate)
                    {
                        plans.Add(new LeasePlan(
                            unitId, 
                            shuffledTenants[tenantIndex++],
                            activeStart,
                            null,
                            RentAmount(),
                            DueDay(), 
                            true));

                        continue;
                    }
                }
            }

            if (canCreateClosedLease && modeRoll < 0.35d)
            {
                var closedStart = RandomDate(options.FromDate, options.ToDate.AddDays(-45));
                var closedEnd = ClampDate(
                    closedStart.AddMonths(_random.Next(3, 13)).AddDays(-1),
                    closedStart.AddDays(14),
                    options.ToDate.AddDays(-7));

                if (closedEnd > closedStart)
                {
                    plans.Add(new LeasePlan(
                        unitId, 
                        shuffledTenants[tenantIndex++], 
                        closedStart,
                        closedEnd,
                        RentAmount(),
                        DueDay(),
                        false));

                    continue;
                }
            }

            var activeLeaseStartMax = options.ToDate.AddDays(-7);
            if (activeLeaseStartMax < options.FromDate)
                activeLeaseStartMax = options.FromDate;

            var activeLeaseStart = RandomDate(options.FromDate, activeLeaseStartMax);
            plans.Add(new LeasePlan(
                unitId,
                shuffledTenants[tenantIndex++],
                activeLeaseStart,
                null,
                RentAmount(),
                DueDay(), 
                true));
        }

        return plans;
    }

    private async Task<List<SeededLease>> SeedLeasesAsync(IReadOnlyList<LeasePlan> plans, CancellationToken ct)
    {
        var list = new List<SeededLease>(plans.Count);
        foreach (var plan in plans)
        {
            var lease = await CreateAndPostDocumentAsync(
                PropertyManagementCodes.Lease,
                ToDateTimeUtc(plan.StartDate),
                Payload(new
                {
                    property_id = plan.UnitId,
                    start_on_utc = plan.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    end_on_utc = plan.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    rent_amount = plan.RentAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    due_day = plan.DueDay,
                },LeaseParts.PrimaryTenant(plan.TenantPartyId)), ct);

            list.Add(new SeededLease(
                lease.Id,
                plan.UnitId,
                plan.TenantPartyId,
                plan.StartDate,
                plan.EndDate,
                plan.RentAmount,
                plan.DueDay,
                plan.IsActive));
        }

        return list;
    }

    private async Task<ReceivablesSummary> SeedReceivablesAsync(
        IReadOnlyList<SeededLease> leases,
        DemoLookup lookup,
        CancellationToken ct)
    {
        var summary = new ReceivablesSummary();

        foreach (var lease in leases)
        {
            var month = new DateOnly(lease.StartDate.Year, lease.StartDate.Month, 1);
            var effectiveLeaseEnd = lease.EndDate is null || lease.EndDate > options.ToDate
                ? options.ToDate
                : lease.EndDate.Value;
            var lastMonth = new DateOnly(effectiveLeaseEnd.Year, effectiveLeaseEnd.Month, 1);

            while (month <= lastMonth)
            {
                var monthStart = month;
                var monthEnd = month.AddMonths(1).AddDays(-1);
                var periodFrom = lease.StartDate > monthStart ? lease.StartDate : monthStart;
                var periodTo = effectiveLeaseEnd < monthEnd ? effectiveLeaseEnd : monthEnd;

                if (periodFrom > periodTo)
                {
                    month = month.AddMonths(1);
                    continue;
                }

                var dueDay = Math.Min(lease.DueDay, DateTime.DaysInMonth(month.Year, month.Month));
                var nominalDueDate = new DateOnly(month.Year, month.Month, dueDay);
                var dueDate = nominalDueDate < periodFrom ? periodFrom : nominalDueDate;

                if (dueDate > options.ToDate)
                {
                    month = month.AddMonths(1);
                    continue;
                }

                var rentCharge = await CreateAndPostDocumentAsync(
                    PropertyManagementCodes.RentCharge,
                    ToDateTimeUtc(dueDate),
                    Payload(new
                    {
                        lease_id = lease.Id,
                        period_from_utc = periodFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        period_to_utc = periodTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        due_on_utc = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        amount = lease.RentAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                        memo = $"Monthly rent for {periodFrom:MMMM yyyy}."
                    }), ct);
                summary.RentChargesPosted++;

                await MaybeCreateReceivableSettlementAsync(
                    lease.Id,
                    rentCharge.Id,
                    dueDate,
                    lease.RentAmount,
                    null,
                    lookup.DefaultBankAccountId,
                    summary,
                    ct: ct);

                if (_random.NextDouble() < 0.28d)
                {
                    var extraTypeId = _random.NextDouble() < 0.72d ? lookup.UtilityChargeTypeId : lookup.ParkingChargeTypeId;
                    var extraAmount = decimal.Round((decimal)(_random.Next(45, 240) + _random.NextDouble()), 4);
                    var extraDueDate = dueDate.AddDays(_random.Next(0, 10));
                    var extraCharge = await CreateAndPostDocumentAsync(
                        PropertyManagementCodes.ReceivableCharge,
                        ToDateTimeUtc(extraDueDate),
                        Payload(new
                        {
                            lease_id = lease.Id,
                            charge_type_id = extraTypeId,
                            due_on_utc = extraDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            amount = extraAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                            memo = $"Resident reimbursement for {extraDueDate:MMMM yyyy}."
                        }),
                        ct);
                    summary.ReceivableChargesPosted++;

                    await MaybeCreateReceivableSettlementAsync(
                        lease.Id,
                        extraCharge.Id,
                        extraDueDate,
                        extraAmount,
                        extraTypeId,
                        lookup.DefaultBankAccountId,
                        summary,
                        ct: ct);
                }

                if (dueDate <= options.ToDate.AddDays(-5) && _random.NextDouble() < 0.09d)
                {
                    var creditMemoDate = dueDate.AddDays(_random.Next(1, 13));
                    if (creditMemoDate > options.ToDate)
                        creditMemoDate = options.ToDate;

                    var creditAmount = Decimal.Round(decimal.Min(lease.RentAmount * 0.35m, _random.Next(25, 180) + (decimal)_random.NextDouble()), 4);
                    await CreateAndPostDocumentAsync(
                        PropertyManagementCodes.ReceivableCreditMemo,
                        ToDateTimeUtc(creditMemoDate),
                        Payload(new
                        {
                            lease_id = lease.Id,
                            charge_type_id = _random.NextDouble() < 0.68d ? lookup.UtilityChargeTypeId : lookup.ParkingChargeTypeId,
                            credited_on_utc = creditMemoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            amount = creditAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                            memo = "Resident credit memo posted for an account adjustment."
                        }),
                        ct);
                    summary.ReceivableCreditMemosPosted++;
                }

                if (dueDate < options.ToDate.AddDays(-35) && _random.NextDouble() < 0.08d)
                {
                    var lateFeeDate = dueDate.AddDays(30);
                    if (lateFeeDate <= options.ToDate)
                    {
                        var lateFeeAmount = Decimal.Round(_random.Next(25, 90), 4);
                        var lateFeeCharge = await CreateAndPostDocumentAsync(
                            PropertyManagementCodes.LateFeeCharge,
                            ToDateTimeUtc(lateFeeDate),
                            Payload(new
                            {
                                lease_id = lease.Id,
                                due_on_utc = lateFeeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                amount = lateFeeAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                                memo = "Late fee for overdue resident balance."
                            }), ct);
                        summary.LateFeeChargesPosted++;

                        await MaybeCreateReceivableSettlementAsync(
                            lease.Id,
                            lateFeeCharge.Id,
                            lateFeeDate, lateFeeAmount,
                            null,
                            lookup.DefaultBankAccountId,
                            summary,
                            0.62d,
                            ct);
                    }
                }

                month = month.AddMonths(1);
            }
        }

        await EnsureReceivableCreditMemoSeededAsync(leases, lookup, summary, ct);
        await EnsureReceivableReturnedPaymentSeededAsync(leases, lookup, summary, ct);
        return summary;
    }

    private async Task MaybeCreateReceivableSettlementAsync(
        Guid leaseId,
        Guid chargeDocumentId,
        DateOnly dueDate,
        decimal amount,
        Guid? extraChargeTypeId,
        Guid defaultBankAccountId,
        ReceivablesSummary summary,
        double baseProbability = 0.90d,
        CancellationToken ct = default)
    {
        var oldEnough = dueDate <= options.ToDate.AddDays(-10);
        if (!oldEnough)
            return;

        var probability = dueDate <= options.ToDate.AddMonths(-2) ? Math.Min(0.98d, baseProbability + 0.05d) : baseProbability;
        if (_random.NextDouble() > probability)
            return;

        var paidFraction = _random.NextDouble() < 0.12d ? (decimal)(_random.Next(55, 95) / 100.0) : 1.0m;
        var paymentAmount = Decimal.Round(amount * paidFraction, 4);
        if (paymentAmount <= 0m)
            return;

        var receivedOn = dueDate.AddDays(_random.Next(-2, 16));
        if (receivedOn < options.FromDate)
            receivedOn = options.FromDate;
        if (receivedOn > options.ToDate)
            receivedOn = options.ToDate;

        var payment = await CreateAndPostDocumentAsync(
            PropertyManagementCodes.ReceivablePayment,
            ToDateTimeUtc(receivedOn),
            Payload(new
            {
                lease_id = leaseId,
                bank_account_id = defaultBankAccountId,
                received_on_utc = receivedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = paymentAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                memo = extraChargeTypeId is null ? "Resident payment received." : "Resident payment received for an additional charge."
            }), ct);
        summary.ReceivablePaymentsPosted++;

        if (receivedOn <= options.ToDate.AddDays(-3) && _random.NextDouble() < 0.05d)
        {
            var returnedOn = receivedOn.AddDays(_random.Next(1, 8));
            if (returnedOn > options.ToDate)
                returnedOn = options.ToDate;

            await CreateAndPostDocumentAsync(
                PropertyManagementCodes.ReceivableReturnedPayment,
                ToDateTimeUtc(returnedOn),
                Payload(new
                {
                    original_payment_id = payment.Id,
                    returned_on_utc = returnedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount = paymentAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                    memo = "Returned payment due to bank reversal."
                }),
                ct);
            summary.ReceivableReturnedPaymentsPosted++;
            return;
        }

        var apply = await CreateAndPostDocumentAsync(
            PropertyManagementCodes.ReceivableApply,
            ToDateTimeUtc(receivedOn),
            Payload(new
            {
                credit_document_id = payment.Id,
                charge_document_id = chargeDocumentId,
                applied_on_utc = receivedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = paymentAmount.ToString("0.0000", CultureInfo.InvariantCulture),
            }), ct);
        _ = apply;
        summary.ReceivableAppliesPosted++;
    }

    private async Task<PayablesSummary> SeedPayablesAsync(
        IReadOnlyList<BuildingSeedResult> buildings,
        IReadOnlyList<PartySeedResult> vendors,
        DemoLookup lookup,
        CancellationToken ct)
    {
        var summary = new PayablesSummary();
        var month = new DateOnly(options.FromDate.Year, options.FromDate.Month, 1);
        var lastMonth = new DateOnly(options.ToDate.Year, options.ToDate.Month, 1);

        while (month <= lastMonth)
        {
            foreach (var building in buildings)
            {
                var chargeCount = _random.Next(0, 3);
                for (var i = 0; i < chargeCount; i++)
                {
                    var vendor = vendors[_random.Next(vendors.Count)];
                    var chargeTypeId = _random.NextDouble() < 0.55d
                        ? lookup.RepairChargeTypeId
                        : lookup.UtilityExpenseChargeTypeId;
                    var dueDate = month.AddDays(_random.Next(0, 20));
                    if (dueDate > options.ToDate)
                        continue;

                    var amount = decimal.Round(_random.Next(120, 2250) + (decimal)_random.NextDouble(), 4);
                    var charge = await CreateAndPostDocumentAsync(
                        PropertyManagementCodes.PayableCharge,
                        ToDateTimeUtc(dueDate),
                        Payload(new
                        {
                            party_id = vendor.Id,
                            property_id = building.BuildingId,
                            charge_type_id = chargeTypeId,
                            vendor_invoice_no = $"{options.DatasetCode}-INV-{month:yyyyMM}-{i + 1:000}",
                            due_on_utc = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            amount = amount.ToString("0.0000", CultureInfo.InvariantCulture),
                            memo = "Vendor invoice received for property operations."
                        }), ct);
                    summary.PayableChargesPosted++;

                    if (dueDate <= options.ToDate.AddDays(-4) && _random.NextDouble() < 0.12d)
                    {
                        var creditedOn = dueDate.AddDays(_random.Next(1, 10));
                        if (creditedOn > options.ToDate)
                            creditedOn = options.ToDate;

                        var creditAmount = Decimal.Round(decimal.Min(amount * 0.45m, _random.Next(30, 260) + (decimal)_random.NextDouble()), 4);
                        await CreateAndPostDocumentAsync(
                            PropertyManagementCodes.PayableCreditMemo,
                            ToDateTimeUtc(creditedOn),
                            Payload(new
                            {
                                party_id = vendor.Id,
                                property_id = building.BuildingId,
                                charge_type_id = chargeTypeId,
                                credited_on_utc = creditedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                amount = creditAmount.ToString("0.0000", CultureInfo.InvariantCulture),
                                memo = "Vendor credit memo recorded for a billing adjustment."
                            }),
                            ct);
                        summary.PayableCreditMemosPosted++;
                    }

                    if (dueDate <= options.ToDate.AddDays(-5) && _random.NextDouble() < 0.88d)
                    {
                        var paidOn = dueDate.AddDays(_random.Next(0, 18));
                        if (paidOn > options.ToDate)
                            paidOn = options.ToDate;

                        var payment = await CreateAndPostDocumentAsync(
                            PropertyManagementCodes.PayablePayment,
                            ToDateTimeUtc(paidOn),
                            Payload(new
                            {
                                party_id = vendor.Id,
                                property_id = building.BuildingId,
                                bank_account_id = PickBankAccountId(lookup),
                                paid_on_utc = paidOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                amount = amount.ToString("0.0000", CultureInfo.InvariantCulture),
                                memo = "Vendor payment issued from an operating bank account."
                            }), ct);
                        summary.PayablePaymentsPosted++;

                        await CreateAndPostDocumentAsync(
                            PropertyManagementCodes.PayableApply,
                            ToDateTimeUtc(paidOn),
                            Payload(new
                            {
                                credit_document_id = payment.Id,
                                charge_document_id = charge.Id,
                                applied_on_utc = paidOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                amount = amount.ToString("0.0000", CultureInfo.InvariantCulture),
                            }), ct);
                        summary.PayableAppliesPosted++;
                    }
                }
            }

            month = month.AddMonths(1);
        }

        await EnsurePayableCreditMemoSeededAsync(buildings, vendors, lookup, summary, ct);
        return summary;
    }

    private async Task<MaintenanceSummary> SeedMaintenanceAsync(
        IReadOnlyList<BuildingSeedResult> buildings,
        IReadOnlyList<PartySeedResult> tenants,
        IReadOnlyList<PartySeedResult> vendors,
        DemoLookup lookup,
        CancellationToken ct)
    {
        var summary = new MaintenanceSummary();
        var unitIds = buildings.SelectMany(x => x.UnitIds).ToList();
        var month = new DateOnly(options.FromDate.Year, options.FromDate.Month, 1);
        var lastMonth = new DateOnly(options.ToDate.Year, options.ToDate.Month, 1);

        while (month <= lastMonth)
        {
            var requestCount = _random.Next(1, Math.Min(6, Math.Max(2, unitIds.Count / 10)) + 1);
            for (var i = 0; i < requestCount; i++)
            {
                var requestedOn = month.AddDays(_random.Next(0, 25));
                if (requestedOn > options.ToDate)
                    continue;

                var request = await CreateAndPostDocumentAsync(
                    PropertyManagementCodes.MaintenanceRequest,
                    ToDateTimeUtc(requestedOn),
                    Payload(new
                    {
                        property_id = unitIds[_random.Next(unitIds.Count)],
                        party_id = tenants[_random.Next(tenants.Count)].Id,
                        category_id = lookup.MaintenanceCategories[_random.Next(lookup.MaintenanceCategories.Count)].Id,
                        priority = _random.NextDouble() < 0.15d ? "high" : "normal",
                        subject = Pick(MaintenanceSubjects),
                        description = "Resident-reported maintenance issue requiring inspection.",
                        requested_at_utc = requestedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }), ct);
                summary.RequestsPosted++;

                if (_random.NextDouble() < 0.82d)
                {
                    var dueBy = requestedOn.AddDays(_random.Next(1, 14));
                    if (dueBy > options.ToDate)
                        dueBy = options.ToDate;

                    var workOrder = await CreateAndPostDocumentAsync(
                        PropertyManagementCodes.WorkOrder,
                        ToDateTimeUtc(requestedOn),
                        Payload(new
                        {
                            request_id = request.Id,
                            assigned_party_id = vendors[_random.Next(vendors.Count)].Id,
                            scope_of_work = "Inspect the issue, perform the required work, and confirm the problem is resolved.",
                            due_by_utc = dueBy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            cost_responsibility = _random.NextDouble() < 0.7d ? "owner" : "tenant"
                        }), ct);
                    summary.WorkOrdersPosted++;

                    if (_random.NextDouble() < 0.76d)
                    {
                        var closedAt = dueBy.AddDays(_random.Next(0, 10));
                        if (closedAt > options.ToDate)
                            closedAt = options.ToDate;

                        await CreateAndPostDocumentAsync(
                            PropertyManagementCodes.WorkOrderCompletion,
                            ToDateTimeUtc(closedAt),
                            Payload(new
                            {
                                work_order_id = workOrder.Id,
                                closed_at_utc = closedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                outcome = _random.NextDouble() < 0.85d ? "completed" : "unable_to_complete",
                                resolution_notes = "Work order was reviewed and closed after the on-site visit."
                            }), ct);
                        summary.CompletionsPosted++;
                    }
                }
            }

            month = month.AddMonths(1);
        }

        return summary;
    }

    private async Task EnsureReceivableCreditMemoSeededAsync(
        IReadOnlyList<SeededLease> leases,
        DemoLookup lookup,
        ReceivablesSummary summary,
        CancellationToken ct)
    {
        if (summary.ReceivableCreditMemosPosted > 0 || leases.Count == 0)
            return;

        var lease = leases[0];
        var creditedOn = lease.StartDate < options.FromDate ? options.FromDate : lease.StartDate;
        if (creditedOn > options.ToDate)
            creditedOn = options.ToDate;

        await CreateAndPostDocumentAsync(
            PropertyManagementCodes.ReceivableCreditMemo,
            ToDateTimeUtc(creditedOn),
            Payload(new
            {
                lease_id = lease.Id,
                charge_type_id = lookup.UtilityChargeTypeId,
                credited_on_utc = creditedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = 35.00m.ToString("0.0000", CultureInfo.InvariantCulture),
                memo = "Resident credit memo seeded for demo coverage."
            }),
            ct);
        summary.ReceivableCreditMemosPosted++;
    }

    private async Task EnsureReceivableReturnedPaymentSeededAsync(
        IReadOnlyList<SeededLease> leases,
        DemoLookup lookup,
        ReceivablesSummary summary,
        CancellationToken ct)
    {
        if (summary.ReceivableReturnedPaymentsPosted > 0 || leases.Count == 0)
            return;

        var lease = leases[0];
        var paymentDate = lease.StartDate < options.FromDate ? options.FromDate : lease.StartDate;
        if (paymentDate > options.ToDate)
            paymentDate = options.ToDate;

        var payment = await CreateAndPostDocumentAsync(
            PropertyManagementCodes.ReceivablePayment,
            ToDateTimeUtc(paymentDate),
            Payload(new
            {
                lease_id = lease.Id,
                bank_account_id = lookup.DefaultBankAccountId,
                received_on_utc = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = 75.00m.ToString("0.0000", CultureInfo.InvariantCulture),
                memo = "Resident payment seeded for returned-payment coverage."
            }),
            ct);
        summary.ReceivablePaymentsPosted++;

        var returnedOn = paymentDate < options.ToDate ? paymentDate.AddDays(1) : paymentDate;
        await CreateAndPostDocumentAsync(
            PropertyManagementCodes.ReceivableReturnedPayment,
            ToDateTimeUtc(returnedOn),
            Payload(new
            {
                original_payment_id = payment.Id,
                returned_on_utc = returnedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = 75.00m.ToString("0.0000", CultureInfo.InvariantCulture),
                memo = "Returned payment seeded for demo coverage."
            }),
            ct);
        summary.ReceivableReturnedPaymentsPosted++;
    }

    private async Task EnsurePayableCreditMemoSeededAsync(
        IReadOnlyList<BuildingSeedResult> buildings,
        IReadOnlyList<PartySeedResult> vendors,
        DemoLookup lookup,
        PayablesSummary summary,
        CancellationToken ct)
    {
        if (summary.PayableCreditMemosPosted > 0 || buildings.Count == 0 || vendors.Count == 0)
            return;

        var creditedOn = options.FromDate;
        await CreateAndPostDocumentAsync(
            PropertyManagementCodes.PayableCreditMemo,
            ToDateTimeUtc(creditedOn),
            Payload(new
            {
                party_id = vendors[0].Id,
                property_id = buildings[0].BuildingId,
                charge_type_id = lookup.RepairChargeTypeId,
                credited_on_utc = creditedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = 55.00m.ToString("0.0000", CultureInfo.InvariantCulture),
                memo = "Vendor credit memo seeded for demo coverage."
            }),
            ct);
        summary.PayableCreditMemosPosted++;
    }

    private async Task<DocumentDto> CreateAndPostDocumentAsync(
        string typeCode,
        DateTime dateUtc,
        RecordPayload payload,
        CancellationToken ct)
    {
        var created = await documents.CreateDraftAsync(typeCode, payload, ct);
        await drafts.UpdateDraftAsync(created.Id, number: null, dateUtc: dateUtc, manageTransaction: true, ct: ct);
        return await documents.PostAsync(typeCode, created.Id, ct);
    }

    private async Task<PeriodClosingSummary> SeedPeriodClosingsAsync(Guid retainedEarningsAccountId, CancellationToken ct)
    {
        var currentYear = timeProvider.GetUtcToday().Year;
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

    private string DatasetMarker() => $"Dataset {options.DatasetCode}";

    private Guid PickBankAccountId(DemoLookup lookup)
        => lookup.BankAccounts[_random.Next(lookup.BankAccounts.Count)].Id;

    private List<PartyIdentity> AllocatePartyIdentities(
        IEnumerable<string> baseDisplays,
        int requiredCount,
        string label)
    {
        var selected = new List<PartyIdentity>(requiredCount);
        var usedDisplays = new HashSet<string>(_usedPartyDisplays, StringComparer.OrdinalIgnoreCase);
        var usedEmails = new HashSet<string>(_usedPartyEmails, StringComparer.OrdinalIgnoreCase);

        foreach (var display in baseDisplays.OrderBy(_ => _random.Next()))
        {
            var identity = CreatePartyIdentity(display);
            if (usedDisplays.Contains(identity.Display) || usedEmails.Contains(identity.Email))
                continue;

            usedDisplays.Add(identity.Display);
            usedEmails.Add(identity.Email);
            selected.Add(identity);

            if (selected.Count == requiredCount)
                break;
        }

        if (selected.Count < requiredCount)
        {
            throw new NgbConfigurationViolationException(
                $"Unable to allocate {requiredCount} clean unique {label} identities. Available unique candidates: {selected.Count}.",
                context: new Dictionary<string, object?>
                {
                    ["label"] = label,
                    ["requested"] = requiredCount,
                    ["allocated"] = selected.Count
                });
        }

        foreach (var identity in selected)
        {
            _usedPartyDisplays.Add(identity.Display);
            _usedPartyEmails.Add(identity.Email);
        }

        return selected;
    }

    private static IEnumerable<string> BuildTenantBaseDisplays()
    {
        foreach (var firstName in TenantFirstNames)
        {
            foreach (var lastName in TenantLastNames)
            {
                yield return $"{firstName} {lastName}";
            }
        }
    }

    private static IEnumerable<string> BuildVendorBaseDisplays()
    {
        foreach (var prefix in VendorPrefixes)
        {
            foreach (var service in VendorServices)
            {
                yield return $"{prefix} {service} LLC";
            }
        }
    }

    private static PartyIdentity CreatePartyIdentity(string display)
    {
        var normalizedDisplay = display.Trim();
        var email = $"{BuildEmailLocalPart(normalizedDisplay)}@ngbplatform.com";
        return new PartyIdentity(normalizedDisplay, email);
    }

    private static string BuildEmailLocalPart(string display)
    {
        var tokens = SplitDisplayTokens(display);
        if (tokens.Count == 0)
            throw new NgbArgumentInvalidException(nameof(display), "Display must contain at least one alphanumeric token.");

        if (tokens.Count > 2 && IsLegalSuffix(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        if (tokens.Count == 0)
            throw new NgbArgumentInvalidException(nameof(display), "Display must contain at least one non-legal token.");

        var first = tokens[0];
        var last = tokens.Count >= 2 ? tokens[^1] : tokens[0];
        return $"{char.ToLowerInvariant(first[0])}.{last.ToLowerInvariant()}";
    }

    private static List<string> SplitDisplayTokens(string display)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in display)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            if (current.Length == 0)
                continue;

            tokens.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static bool IsLegalSuffix(string token)
        => token.Equals("LLC", StringComparison.OrdinalIgnoreCase)
           || token.Equals("INC", StringComparison.OrdinalIgnoreCase)
           || token.Equals("LTD", StringComparison.OrdinalIgnoreCase)
           || token.Equals("CORP", StringComparison.OrdinalIgnoreCase)
           || token.Equals("CO", StringComparison.OrdinalIgnoreCase);

    private string Pick(string[] items) => items[_random.Next(items.Length)];

    private int DueDay() => _random.Next(1, 11);

    private decimal RentAmount() => decimal.Round(_random.Next(950, 3250), 2);

    private static DateOnly ClampDate(DateOnly value, DateOnly minInclusive, DateOnly maxInclusive)
    {
        if (maxInclusive < minInclusive)
            return minInclusive;

        if (value < minInclusive)
            return minInclusive;

        if (value > maxInclusive)
            return maxInclusive;

        return value;
    }

    private DateOnly RandomDate(DateOnly minInclusive, DateOnly maxInclusive)
    {
        if (minInclusive > maxInclusive)
            return minInclusive;

        var delta = maxInclusive.DayNumber - minInclusive.DayNumber;
        return minInclusive.AddDays(delta == 0 ? 0 : _random.Next(0, delta + 1));
    }

    private static DateTime ToDateTimeUtc(DateOnly date)
        => new(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields, Json);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        return new RecordPayload(dict, parts);
    }

    private static string DemoPhone(int index) => $"201-555-{(index % 10_000):0000}";

    private static class LeaseParts
    {
        public static IReadOnlyDictionary<string, RecordPartPayload> PrimaryTenant(Guid partyId)
            => new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new(
                [
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement(partyId, Json),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant", Json),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true, Json),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1, Json)
                    }
                ])
            };
    }

    private sealed record LookupRow(Guid Id, string Name);

    private sealed record DemoBankAccountSeed(
        string AccountCode,
        string AccountName,
        string Display,
        string BankName,
        string Last4);

    private sealed record PartyIdentity(string Display, string Email);

    private sealed record PartySeedResult(Guid Id, string Display);

    private sealed record BuildingSeedResult(Guid BuildingId, IReadOnlyList<Guid> UnitIds);
    
    private sealed record LeasePlan(
        Guid UnitId,
        Guid TenantPartyId,
        DateOnly StartDate,
        DateOnly? EndDate,
        decimal RentAmount,
        int DueDay,
        bool IsActive);

    private sealed record SeededLease(
        Guid Id,
        Guid UnitId,
        Guid TenantPartyId,
        DateOnly StartDate,
        DateOnly? EndDate,
        decimal RentAmount,
        int DueDay,
        bool IsActive);

    private sealed record DemoLookup(
        Guid DefaultBankAccountId,
        IReadOnlyList<LookupRow> BankAccounts,
        Guid UtilityChargeTypeId,
        Guid ParkingChargeTypeId,
        Guid RepairChargeTypeId,
        Guid UtilityExpenseChargeTypeId,
        IReadOnlyList<LookupRow> MaintenanceCategories);

    private sealed record PeriodClosingSummary(int MonthsClosed, int FiscalYearsClosed);

    private sealed class ReceivablesSummary
    {
        public int RentChargesPosted { get; set; }
        public int ReceivableChargesPosted { get; set; }
        public int LateFeeChargesPosted { get; set; }
        public int ReceivableCreditMemosPosted { get; set; }
        public int ReceivablePaymentsPosted { get; set; }
        public int ReceivableReturnedPaymentsPosted { get; set; }
        public int ReceivableAppliesPosted { get; set; }
    }

    private sealed class PayablesSummary
    {
        public int PayableChargesPosted { get; set; }
        public int PayableCreditMemosPosted { get; set; }
        public int PayablePaymentsPosted { get; set; }
        public int PayableAppliesPosted { get; set; }
    }

    private sealed class MaintenanceSummary
    {
        public int RequestsPosted { get; set; }
        public int WorkOrdersPosted { get; set; }
        public int CompletionsPosted { get; set; }
    }
}
