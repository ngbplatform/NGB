using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Contracts;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Migrator.Seed;

internal static class AgencyBillingSeedDemoCli
{
    private const string CommandName = "seed-demo";

    public static bool IsSeedDemoCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static string[] TrimCommand(string[] args) => args.Length <= 1 ? [] : args[1..];

    public static async Task<int> RunAsync(string[] args, TimeProvider? timeProvider = null)
    {
        AgencyBillingDemoSeedOptions? options = null;

        try
        {
            var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
            options = AgencyBillingDemoSeedOptions.Parse(args, DateOnly.FromDateTime(effectiveTimeProvider.GetUtcNow().UtcDateTime));

            var services = new ServiceCollection();
            services.AddLogging();

            services
                .AddNgbRuntime()
                .AddNgbPostgres(options.ConnectionString)
                .AddAgencyBillingModule()
                .AddAgencyBillingRuntimeModule()
                .AddAgencyBillingPostgresModule();
            services.AddSingleton(effectiveTimeProvider);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            AgencyBillingSetupResult setupResult;
            await using (var setupScope = provider.CreateAsyncScope())
            {
                var setupService = setupScope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
                setupResult = await setupService.EnsureDefaultsAsync();
            }

            await using var seedScope = provider.CreateAsyncScope();
            var seeder = new AgencyBillingDemoSeeder(
                options,
                setupResult,
                seedScope.ServiceProvider.GetRequiredService<DefinitionsRegistry>(),
                seedScope.ServiceProvider.GetRequiredService<ICatalogService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentService>(),
                seedScope.ServiceProvider.GetRequiredService<IDocumentDraftService>());

            var summary = await seeder.RunAsync();
            PrintSummary(summary);
            return 0;
        }
        catch (AgencyBillingSeedActivityAlreadyExistsException) when (options?.SkipIfActivityExists == true)
        {
            Console.WriteLine("OK: agency billing demo seed skipped because activity already exists.");
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("FAILED: agency billing seed-demo error.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintSummary(AgencyBillingDemoSeedSummary summary)
    {
        Console.WriteLine("OK: agency billing demo data seeded.");
        Console.WriteLine($"- Period: {summary.FromDate:yyyy-MM-dd} .. {summary.ToDate:yyyy-MM-dd}");
        Console.WriteLine($"- Clients seeded: {summary.ClientsSeeded}");
        Console.WriteLine($"- Team Members seeded: {summary.TeamMembersSeeded}");
        Console.WriteLine($"- Projects seeded: {summary.ProjectsSeeded}");
        Console.WriteLine($"- Service Items seeded: {summary.ServiceItemsSeeded}");
        Console.WriteLine($"- Rate Cards seeded: {summary.RateCardsSeeded}");
        Console.WriteLine($"- Document seed mode: {(summary.DocumentsPosted ? "Posted" : "Draft")}");
        if (!summary.DocumentsPosted)
            Console.WriteLine("- Note: Agency Billing posting handlers are not configured yet, so demo documents were seeded as drafts.");
        Console.WriteLine($"- Client Contract documents seeded: {summary.ClientContractsSeeded}");
        Console.WriteLine($"- Timesheet documents seeded: {summary.TimesheetsSeeded}");
        Console.WriteLine($"- Sales Invoice documents seeded: {summary.SalesInvoicesSeeded}");
        Console.WriteLine($"- Customer Payment documents seeded: {summary.CustomerPaymentsSeeded}");
        Console.WriteLine($"- Total Agency Billing documents seeded: {summary.TotalDocumentsSeeded}");
    }
}

internal sealed class AgencyBillingSeedActivityAlreadyExistsException()
    : NgbConflictException(
        message: "Agency Billing seed-demo expects a clean Agency Billing activity ledger. Existing Agency Billing documents were found.",
        errorCode: ErrorCodeConst)
{
    public const string ErrorCodeConst = "ab.seed_demo.activity_exists";
}

internal sealed record AgencyBillingDemoSeedOptions(
    string ConnectionString,
    int Seed,
    DateOnly FromDate,
    DateOnly ToDate,
    int Clients,
    int TeamMembers,
    int Projects,
    int Timesheets,
    int SalesInvoices,
    int CustomerPayments,
    bool SkipIfActivityExists)
{
    public static AgencyBillingDemoSeedOptions Parse(string[] args, DateOnly todayUtc)
    {
        var connectionString = AgencyBillingSeedCliArgs.RequireConnectionString(args);
        var seed = AgencyBillingSeedCliArgs.GetInt(args, "--seed", 20260416);
        var fromDate = AgencyBillingSeedCliArgs.GetDateOnly(args, "--from", new DateOnly(2025, 1, 1));
        var toDate = AgencyBillingSeedCliArgs.GetDateOnly(args, "--to", todayUtc);
        var clients = AgencyBillingSeedCliArgs.GetInt(args, "--clients", 6);
        var teamMembers = AgencyBillingSeedCliArgs.GetInt(args, "--team-members", 10);
        var projects = AgencyBillingSeedCliArgs.GetInt(args, "--projects", 8);
        var timesheets = AgencyBillingSeedCliArgs.GetInt(args, "--timesheets", 96);
        var salesInvoices = AgencyBillingSeedCliArgs.GetInt(args, "--sales-invoices", 18);
        var customerPayments = AgencyBillingSeedCliArgs.GetInt(args, "--customer-payments", 14);
        var skipIfActivityExists = AgencyBillingSeedCliArgs.GetBool(args, "--skip-if-activity-exists", false);

        if (fromDate > toDate)
            throw new NgbArgumentInvalidException("--from", "'--from' must be less than or equal to '--to'.");

        ValidateRange("--clients", clients, 1, 500);
        ValidateRange("--team-members", teamMembers, 2, 500);
        ValidateRange("--projects", projects, 1, 1000);
        ValidateRange("--timesheets", timesheets, 1, 50000);
        ValidateRange("--sales-invoices", salesInvoices, 0, timesheets);
        ValidateRange("--customer-payments", customerPayments, 0, salesInvoices);

        return new AgencyBillingDemoSeedOptions(
            connectionString,
            seed,
            fromDate,
            toDate,
            clients,
            teamMembers,
            projects,
            timesheets,
            salesInvoices,
            customerPayments,
            skipIfActivityExists);
    }

    private static void ValidateRange(string name, int value, int min, int max)
    {
        if (value < min || value > max)
            throw new NgbArgumentOutOfRangeException(name, value, $"'{name}' must be between {min} and {max}.");
    }
}

internal sealed record AgencyBillingDemoSeedSummary(
    DateOnly FromDate,
    DateOnly ToDate,
    int ClientsSeeded,
    int TeamMembersSeeded,
    int ProjectsSeeded,
    int ServiceItemsSeeded,
    int RateCardsSeeded,
    bool DocumentsPosted,
    int ClientContractsSeeded,
    int TimesheetsSeeded,
    int SalesInvoicesSeeded,
    int CustomerPaymentsSeeded)
{
    public int TotalDocumentsSeeded => ClientContractsSeeded + TimesheetsSeeded + SalesInvoicesSeeded + CustomerPaymentsSeeded;
}

internal sealed class AgencyBillingDemoSeeder(
    AgencyBillingDemoSeedOptions options,
    AgencyBillingSetupResult setup,
    DefinitionsRegistry definitions,
    ICatalogService catalogs,
    IDocumentService documents,
    IDocumentDraftService drafts)
{
    private static readonly string[] AgencyDocumentTypes =
    [
        AgencyBillingCodes.ClientContract,
        AgencyBillingCodes.Timesheet,
        AgencyBillingCodes.SalesInvoice,
        AgencyBillingCodes.CustomerPayment
    ];

    private static readonly ServiceItemTemplate[] ServiceItemTemplates =
    [
        new("STRATEGY", "Strategy", AgencyBillingServiceItemUnitOfMeasure.Hour),
        new("CREATIVE", "Creative Direction", AgencyBillingServiceItemUnitOfMeasure.Hour),
        new("DESIGN", "Design", AgencyBillingServiceItemUnitOfMeasure.Hour),
        new("COPY", "Copywriting", AgencyBillingServiceItemUnitOfMeasure.Hour),
        new("IMPLEMENTATION", "Implementation", AgencyBillingServiceItemUnitOfMeasure.Hour),
        new("ANALYTICS", "Analytics", AgencyBillingServiceItemUnitOfMeasure.Hour)
    ];

    private static readonly string[] ClientPrefixes =
    [
        "Northwind", "Contoso", "Fabrikam", "Tailspin", "Adventure Works", "Blue Yonder",
        "Apex", "Summit", "Brightline", "Oakridge", "Harbor Point", "Greenfield"
    ];

    private static readonly string[] ClientSuffixes =
    [
        "Creative", "Advisory", "Studio", "Media", "Commerce", "Hospitality",
        "Health", "Retail", "Capital", "Logistics", "Labs", "Collective"
    ];

    private static readonly string[] FirstNames =
    [
        "Ava", "Liam", "Sophia", "Noah", "Olivia", "Ethan", "Emma", "Lucas",
        "Mia", "James", "Charlotte", "Benjamin", "Amelia", "Henry", "Harper", "Jack"
    ];

    private static readonly string[] LastNames =
    [
        "Stone", "Carter", "Patel", "Kim", "Diaz", "Morris", "Lopez", "Nguyen",
        "Turner", "Reed", "Brooks", "Hayes", "Ross", "Bennett", "Powell", "Sullivan"
    ];

    private static readonly string[] TeamTitles =
    [
        "Engagement Manager", "Senior Consultant", "Creative Lead", "Growth Strategist",
        "Delivery Consultant", "Analytics Lead", "Project Director", "Implementation Specialist"
    ];

    private static readonly string[] ProjectThemes =
    [
        "Brand Refresh", "Growth Sprint", "Retention Program", "Launch Readiness",
        "Revenue Operations", "Performance Audit", "Commerce Optimization", "CRM Rollout",
        "Demand Gen Accelerator", "Lifecycle Overhaul", "Content Engine", "Reporting Modernization"
    ];

    private static readonly string[] TimesheetActivities =
    [
        "Discovery workshop", "Sprint planning", "Stakeholder review", "Delivery session",
        "Creative revision", "Implementation support", "Performance analysis", "Client enablement"
    ];

    private readonly Random _random = new(options.Seed);

    public async Task<AgencyBillingDemoSeedSummary> RunAsync(CancellationToken ct = default)
    {
        await EnsureAgencyBillingActivityDoesNotExistAsync(ct);
        var postDocuments = CanPostAgencyDocuments();

        var paymentTerms = await LoadPaymentTermsAsync(ct);
        var serviceItems = await SeedServiceItemsAsync(ct);
        var clients = await SeedClientsAsync(paymentTerms, ct);
        var teamMembers = await SeedTeamMembersAsync(ct);
        var projects = await SeedProjectsAsync(clients, teamMembers, serviceItems, ct);

        var rateCardsSeeded = await SeedRateCardsAsync(projects, ct);
        var contracts = await SeedContractsAsync(projects, postDocuments, ct);
        var timesheets = await SeedTimesheetsAsync(contracts, postDocuments, ct);
        var invoices = await SeedSalesInvoicesAsync(timesheets, postDocuments, ct);
        var payments = await SeedCustomerPaymentsAsync(invoices, postDocuments, ct);

        return new AgencyBillingDemoSeedSummary(
            options.FromDate,
            options.ToDate,
            clients.Count,
            teamMembers.Count,
            projects.Count,
            serviceItems.Count,
            rateCardsSeeded,
            postDocuments,
            contracts.Count,
            timesheets.Count,
            invoices.Count,
            payments.Count);
    }

    private async Task EnsureAgencyBillingActivityDoesNotExistAsync(CancellationToken ct)
    {
        foreach (var typeCode in AgencyDocumentTypes)
        {
            var page = await documents.GetPageAsync(typeCode, new PageRequestDto(Offset: 0, Limit: 1, Search: null), ct);
            if (page.Total.GetValueOrDefault(page.Items.Count) > 0)
                throw new AgencyBillingSeedActivityAlreadyExistsException();
        }
    }

    private async Task<IReadOnlyList<PaymentTermSeed>> LoadPaymentTermsAsync(CancellationToken ct)
        =>
        [
            new PaymentTermSeed("Due on Receipt", await GetCatalogIdByDisplayAsync(AgencyBillingCodes.PaymentTerms, "Due on Receipt", ct), 0),
            new PaymentTermSeed("Net 15", await GetCatalogIdByDisplayAsync(AgencyBillingCodes.PaymentTerms, "Net 15", ct), 15),
            new PaymentTermSeed("Net 30", await GetCatalogIdByDisplayAsync(AgencyBillingCodes.PaymentTerms, "Net 30", ct), 30)
        ];

    private async Task<IReadOnlyList<ServiceItemSeed>> SeedServiceItemsAsync(CancellationToken ct)
    {
        var result = new List<ServiceItemSeed>(ServiceItemTemplates.Length);

        for (var i = 0; i < ServiceItemTemplates.Length; i++)
        {
            var template = ServiceItemTemplates[i];
            var display = template.Name;
            var id = await UpsertCatalogByDisplayAsync(
                AgencyBillingCodes.ServiceItem,
                display,
                Payload(new
                {
                    display,
                    code = template.Code,
                    name = template.Name,
                    unit_of_measure = (int)template.UnitOfMeasure,
                    default_revenue_account_id = setup.ServiceRevenueAccountId,
                    is_active = true,
                    notes = (string?)null
                }),
                ct);

            result.Add(new ServiceItemSeed(id, template.Code, template.Name, template.UnitOfMeasure));
        }

        return result;
    }

    private async Task<IReadOnlyList<ClientSeed>> SeedClientsAsync(
        IReadOnlyList<PaymentTermSeed> paymentTerms,
        CancellationToken ct)
    {
        var result = new List<ClientSeed>(options.Clients);

        for (var i = 0; i < options.Clients; i++)
        {
            var display = BuildCompanyName(ClientPrefixes, ClientSuffixes, i);
            var paymentTerm = paymentTerms[i % paymentTerms.Count];
            var clientCode = $"CLI-{1000 + i}";
            var emailSlug = NormalizeEmailSlug(display);
            var billingContact = BuildPersonName(i + 100);

            var id = await UpsertCatalogByDisplayAsync(
                AgencyBillingCodes.Client,
                display,
                Payload(new
                {
                    display,
                    client_code = clientCode,
                    name = display,
                    legal_name = $"{display} LLC",
                    status = (int)AgencyBillingClientStatus.Active,
                    email = $"ap@{emailSlug}.example",
                    phone = DemoPhone(i),
                    billing_contact = billingContact,
                    payment_terms_id = paymentTerm.Id,
                    default_currency = AgencyBillingCodes.DefaultCurrency,
                    is_active = true,
                    notes = (string?)null
                }),
                ct);

            result.Add(new ClientSeed(id, display, clientCode, paymentTerm));
        }

        return result;
    }

    private async Task<IReadOnlyList<TeamMemberSeed>> SeedTeamMembersAsync(CancellationToken ct)
    {
        var result = new List<TeamMemberSeed>(options.TeamMembers);

        for (var i = 0; i < options.TeamMembers; i++)
        {
            var fullName = BuildPersonName(i);
            var memberCode = $"TM-{1000 + i}";
            var defaultBillingRate = 135m + (i % 6) * 15m;
            var defaultCostRate = 52m + (i % 5) * 8m;
            var memberType = i % 4 == 0
                ? AgencyBillingTeamMemberType.Contractor
                : AgencyBillingTeamMemberType.Employee;
            var title = TeamTitles[i % TeamTitles.Length];
            var id = await UpsertCatalogByDisplayAsync(
                AgencyBillingCodes.TeamMember,
                fullName,
                Payload(new
                {
                    display = fullName,
                    member_code = memberCode,
                    full_name = fullName,
                    member_type = (int)memberType,
                    is_active = true,
                    billable_by_default = true,
                    default_billing_rate = defaultBillingRate,
                    default_cost_rate = defaultCostRate,
                    email = $"{NormalizeEmailSlug(fullName)}@demo.ngbplatform.com",
                    title
                }),
                ct);

            result.Add(new TeamMemberSeed(id, fullName, memberCode, title, defaultBillingRate, defaultCostRate));
        }

        return result;
    }

    private async Task<IReadOnlyList<ProjectSeed>> SeedProjectsAsync(
        IReadOnlyList<ClientSeed> clients,
        IReadOnlyList<TeamMemberSeed> teamMembers,
        IReadOnlyList<ServiceItemSeed> serviceItems,
        CancellationToken ct)
    {
        var result = new List<ProjectSeed>(options.Projects);

        for (var i = 0; i < options.Projects; i++)
        {
            var client = clients[i % clients.Count];
            var manager = teamMembers[i % teamMembers.Count];
            var assignmentCount = Math.Min(serviceItems.Count >= 3 ? 3 : 2, Math.Min(teamMembers.Count, serviceItems.Count));
            var assignments = new List<ProjectAssignmentSeed>(assignmentCount);

            for (var j = 0; j < assignmentCount; j++)
            {
                var teamMember = teamMembers[(i + j) % teamMembers.Count];
                var serviceItem = serviceItems[(i + j) % serviceItems.Count];
                var billingRate = Math.Max(teamMember.DefaultBillingRate + (j * 10m), 150m + (j * 12m));
                var costRate = teamMember.DefaultCostRate + (j * 3m);

                assignments.Add(new ProjectAssignmentSeed(teamMember, serviceItem, RoundMoney(billingRate), RoundMoney(costRate)));
            }

            var projectName = $"{client.Display} {ProjectThemes[i % ProjectThemes.Length]}";
            var projectCode = $"PRJ-{2000 + i}";
            var startDate = options.FromDate.AddDays(Math.Min(i * 7, Math.Max(0, options.ToDate.DayNumber - options.FromDate.DayNumber)));
            var budgetHours = 220m + (i % 5) * 60m;
            var averageBillingRate = assignments.Average(x => x.BillingRate);
            var budgetAmount = RoundMoney(budgetHours * averageBillingRate);

            var id = await UpsertCatalogByDisplayAsync(
                AgencyBillingCodes.Project,
                projectName,
                Payload(new
                {
                    display = projectName,
                    project_code = projectCode,
                    name = projectName,
                    client_id = client.Id,
                    project_manager_id = manager.Id,
                    start_date = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    status = (int)AgencyBillingProjectStatus.Active,
                    billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials,
                    budget_hours = budgetHours,
                    budget_amount = budgetAmount,
                    notes = (string?)null
                }),
                ct);

            result.Add(new ProjectSeed(id, projectName, projectCode, client, manager, assignments, startDate));
        }

        return result;
    }

    private async Task<int> SeedRateCardsAsync(IReadOnlyList<ProjectSeed> projects, CancellationToken ct)
    {
        var count = 0;

        foreach (var project in projects)
        {
            foreach (var assignment in project.Assignments)
            {
                var display = $"{project.Display} / {assignment.TeamMember.Display} / {assignment.ServiceItem.Display}";
                await UpsertCatalogByDisplayAsync(
                    AgencyBillingCodes.RateCard,
                    display,
                    Payload(new
                    {
                        display,
                        name = display,
                        client_id = project.Client.Id,
                        project_id = project.Id,
                        team_member_id = assignment.TeamMember.Id,
                        service_item_id = assignment.ServiceItem.Id,
                        service_title = assignment.ServiceItem.Display,
                        billing_rate = assignment.BillingRate,
                        cost_rate = assignment.CostRate,
                        effective_from = project.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        is_active = true,
                        notes = (string?)null
                    }),
                    ct);

                count++;
            }
        }

        return count;
    }

    private async Task<IReadOnlyList<ContractSeed>> SeedContractsAsync(
        IReadOnlyList<ProjectSeed> projects,
        bool postDocuments,
        CancellationToken ct)
    {
        var result = new List<ContractSeed>(projects.Count);

        foreach (var project in projects.OrderBy(x => x.StartDate))
        {
            var effectiveFrom = project.StartDate < options.FromDate ? options.FromDate : project.StartDate;
            var contractId = (await CreateSeededDocumentAsync(
                AgencyBillingCodes.ClientContract,
                effectiveFrom,
                Payload(
                    new
                    {
                        effective_from = effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        client_id = project.Client.Id,
                        project_id = project.Id,
                        currency_code = AgencyBillingCodes.DefaultCurrency,
                        billing_frequency = (int)AgencyBillingContractBillingFrequency.Monthly,
                        payment_terms_id = project.Client.PaymentTerms.Id,
                        invoice_memo_template = $"Professional services for {project.Display}",
                        is_active = true,
                        notes = (string?)null
                    },
                    "lines",
                    project.Assignments.Select((assignment, index) => new
                    {
                        ordinal = index + 1,
                        service_item_id = assignment.ServiceItem.Id,
                        team_member_id = assignment.TeamMember.Id,
                        service_title = assignment.ServiceItem.Display,
                        billing_rate = assignment.BillingRate,
                        cost_rate = assignment.CostRate,
                        active_from = effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        notes = (string?)null
                    })),
                postDocuments,
                ct)).Id;

            result.Add(new ContractSeed(contractId, project, effectiveFrom));
        }

        return result;
    }

    private async Task<IReadOnlyList<TimesheetSeed>> SeedTimesheetsAsync(
        IReadOnlyList<ContractSeed> contracts,
        bool postDocuments,
        CancellationToken ct)
    {
        var plans = new List<TimesheetPlan>(options.Timesheets);

        for (var i = 0; i < options.Timesheets; i++)
        {
            var contract = contracts[i % contracts.Count];
            var assignment = contract.Project.Assignments[(i + _random.Next(contract.Project.Assignments.Count)) % contract.Project.Assignments.Count];
            var workDate = RandomBusinessDate(MaxDate(options.FromDate, contract.EffectiveFrom), options.ToDate);
            var hours = QuarterHours(8, 32);
            var amount = RoundMoney(hours * assignment.BillingRate);
            var costAmount = RoundMoney(hours * assignment.CostRate);
            var description = $"{TimesheetActivities[i % TimesheetActivities.Length]} for {assignment.ServiceItem.Display.ToLowerInvariant()}";

            plans.Add(new TimesheetPlan(contract, assignment, workDate, hours, amount, costAmount, description));
        }

        var result = new List<TimesheetSeed>(plans.Count);
        foreach (var plan in plans.OrderBy(x => x.WorkDate).ThenBy(x => x.Contract.Project.Display, StringComparer.OrdinalIgnoreCase))
        {
            var seeded = await CreateSeededDocumentAsync(
                AgencyBillingCodes.Timesheet,
                plan.WorkDate,
                Payload(
                    new
                    {
                        document_date_utc = plan.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        team_member_id = plan.Assignment.TeamMember.Id,
                        project_id = plan.Contract.Project.Id,
                        client_id = plan.Contract.Project.Client.Id,
                        work_date = plan.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        total_hours = plan.Hours,
                        amount = plan.Amount,
                        cost_amount = plan.CostAmount,
                        notes = (string?)null
                    },
                    "lines",
                    [
                        new
                        {
                            ordinal = 1,
                            service_item_id = plan.Assignment.ServiceItem.Id,
                            description = plan.Description,
                            hours = plan.Hours,
                            billable = true,
                            billing_rate = plan.Assignment.BillingRate,
                            cost_rate = plan.Assignment.CostRate,
                            line_amount = plan.Amount,
                            line_cost_amount = plan.CostAmount
                        }
                    ]),
                postDocuments,
                ct);

            result.Add(new TimesheetSeed(
                seeded.Id,
                plan.Contract,
                plan.Assignment,
                plan.WorkDate,
                plan.Hours,
                plan.Amount,
                plan.Description));
        }

        return result;
    }

    private async Task<IReadOnlyList<SalesInvoiceSeed>> SeedSalesInvoicesAsync(
        IReadOnlyList<TimesheetSeed> timesheets,
        bool postDocuments,
        CancellationToken ct)
    {
        if (options.SalesInvoices == 0)
            return [];

        var selectedTimesheets = timesheets
            .OrderBy(_ => _random.Next())
            .Take(options.SalesInvoices)
            .OrderBy(x => x.WorkDate)
            .ThenBy(x => x.Contract.Project.Display, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<SalesInvoiceSeed>(selectedTimesheets.Length);
        foreach (var (timesheet, index) in selectedTimesheets.Select((value, idx) => (value, idx)))
        {
            var invoiceDate = RandomBusinessDate(timesheet.WorkDate, MaxDate(timesheet.WorkDate, options.ToDate));
            var dueDate = invoiceDate.AddDays(timesheet.Contract.Project.Client.PaymentTerms.DueDays);
            var description = $"{timesheet.Assignment.ServiceItem.Display} services delivered on {timesheet.WorkDate:yyyy-MM-dd}";
            var seeded = await CreateSeededDocumentAsync(
                AgencyBillingCodes.SalesInvoice,
                invoiceDate,
                Payload(
                    new
                    {
                        document_date_utc = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        due_date = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        client_id = timesheet.Contract.Project.Client.Id,
                        project_id = timesheet.Contract.Project.Id,
                        contract_id = timesheet.Contract.Id,
                        currency_code = AgencyBillingCodes.DefaultCurrency,
                        memo = $"Seeded invoice {index + 1} for {timesheet.Contract.Project.Display}",
                        amount = timesheet.Amount,
                        notes = (string?)null
                    },
                    "lines",
                    [
                        new
                        {
                            ordinal = 1,
                            service_item_id = timesheet.Assignment.ServiceItem.Id,
                            source_timesheet_id = timesheet.Id,
                            description,
                            quantity_hours = timesheet.Hours,
                            rate = timesheet.Assignment.BillingRate,
                            line_amount = timesheet.Amount
                        }
                    ]),
                postDocuments,
                ct);

            result.Add(new SalesInvoiceSeed(
                seeded.Id,
                timesheet.Contract,
                invoiceDate,
                dueDate,
                timesheet.Amount));
        }

        return result;
    }

    private async Task<IReadOnlyList<CustomerPaymentSeed>> SeedCustomerPaymentsAsync(
        IReadOnlyList<SalesInvoiceSeed> invoices,
        bool postDocuments,
        CancellationToken ct)
    {
        if (options.CustomerPayments == 0)
            return [];

        var selectedInvoices = invoices
            .OrderBy(_ => _random.Next())
            .Take(options.CustomerPayments)
            .OrderBy(x => x.InvoiceDate)
            .ThenBy(x => x.Contract.Project.Client.Display, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<CustomerPaymentSeed>(selectedInvoices.Length);
        foreach (var (invoice, index) in selectedInvoices.Select((value, idx) => (value, idx)))
        {
            var maxPaymentDate = MinDate(invoice.DueDate, options.ToDate);
            var paymentDate = RandomBusinessDate(invoice.InvoiceDate, MaxDate(invoice.InvoiceDate, maxPaymentDate));
            var factor = index % 4 switch
            {
                0 => 1.00m,
                1 => 0.90m,
                2 => 0.75m,
                _ => 0.60m
            };
            var appliedAmount = RoundMoney(Math.Max(50m, invoice.Amount * factor));
            if (appliedAmount > invoice.Amount)
                appliedAmount = invoice.Amount;

            var seeded = await CreateSeededDocumentAsync(
                AgencyBillingCodes.CustomerPayment,
                paymentDate,
                Payload(
                    new
                    {
                        document_date_utc = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        client_id = invoice.Contract.Project.Client.Id,
                        cash_account_id = setup.CashAccountId,
                        reference_number = $"ACH-{52000 + index}",
                        amount = appliedAmount,
                        notes = (string?)null
                    },
                    "applies",
                    [
                        new
                        {
                            ordinal = 1,
                            sales_invoice_id = invoice.Id,
                            applied_amount = appliedAmount
                        }
                    ]),
                postDocuments,
                ct);

            result.Add(new CustomerPaymentSeed(seeded.Id));
        }

        return result;
    }

    private async Task<Guid> UpsertCatalogByDisplayAsync(
        string catalogType,
        string display,
        RecordPayload payload,
        CancellationToken ct)
    {
        var existing = await FindCatalogByDisplayAsync(catalogType, display, ct);
        if (existing is null)
            return (await catalogs.CreateAsync(catalogType, payload, ct)).Id;

        return (await catalogs.UpdateAsync(catalogType, existing.Id, payload, ct)).Id;
    }

    private async Task<CatalogItemDto?> FindCatalogByDisplayAsync(
        string catalogType,
        string display,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(Offset: 0, Limit: 50, Search: display), ct);
        var matches = page.Items
            .Where(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.")
        };
    }

    private async Task<Guid> GetCatalogIdByDisplayAsync(string catalogType, string display, CancellationToken ct)
    {
        var existing = await FindCatalogByDisplayAsync(catalogType, display, ct);
        return existing?.Id
            ?? throw new NgbConfigurationViolationException($"Default '{catalogType}' record '{display}' was not found.");
    }

    private bool CanPostAgencyDocuments()
        => AgencyDocumentTypes.All(IsDocumentPostable);

    private bool IsDocumentPostable(string typeCode)
    {
        if (!definitions.TryGetDocument(typeCode, out var definition))
            return false;

        return definition.PostingHandlerType is not null
               || definition.OperationalRegisterPostingHandlerType is not null
               || definition.ReferenceRegisterPostingHandlerType is not null;
    }

    private async Task<DocumentDto> CreateSeededDocumentAsync(
        string typeCode,
        DateOnly businessDate,
        RecordPayload payload,
        bool postDocuments,
        CancellationToken ct)
    {
        var created = await documents.CreateDraftAsync(typeCode, payload, ct);
        await drafts.UpdateDraftAsync(
            created.Id,
            number: null,
            dateUtc: ToDateTimeUtc(businessDate),
            manageTransaction: true,
            ct: ct);

        if (postDocuments)
            return await documents.PostAsync(typeCode, created.Id, ct);

        return await documents.GetByIdAsync(typeCode, created.Id, ct);
    }

    private static RecordPayload Payload(object head, string? partName = null, IEnumerable<object>? partRows = null)
    {
        var fields = JsonSerializer.SerializeToElement(head).EnumerateObject().ToDictionary(
            static x => x.Name,
            static x => x.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

        Dictionary<string, RecordPartPayload>? parts = null;
        if (!string.IsNullOrWhiteSpace(partName) && partRows is not null)
        {
            var rows = partRows.Select(row =>
                    JsonSerializer.SerializeToElement(row).EnumerateObject().ToDictionary(
                        static x => x.Name,
                        static x => x.Value.Clone(),
                        StringComparer.OrdinalIgnoreCase))
                .Cast<IReadOnlyDictionary<string, JsonElement>>()
                .ToArray();

            parts = new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                [partName] = new RecordPartPayload(rows)
            };
        }

        return new RecordPayload(fields, parts);
    }

    private static DateTime ToDateTimeUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private decimal QuarterHours(int minQuartersInclusive, int maxQuartersInclusive)
        => _random.Next(minQuartersInclusive, maxQuartersInclusive + 1) / 4m;

    private DateOnly RandomBusinessDate(DateOnly from, DateOnly to)
    {
        if (from > to)
            return from;

        var span = to.DayNumber - from.DayNumber + 1;
        for (var attempt = 0; attempt < Math.Max(7, span * 2); attempt++)
        {
            var candidate = from.AddDays(_random.Next(span));
            if (!IsWeekend(candidate))
                return candidate;
        }

        return NextBusinessDate(from);
    }

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly NextBusinessDate(DateOnly date)
    {
        var candidate = date;
        while (IsWeekend(candidate))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static DateOnly MaxDate(DateOnly left, DateOnly right) => left >= right ? left : right;
    private static DateOnly MinDate(DateOnly left, DateOnly right) => left <= right ? left : right;

    private static string DemoPhone(int index) => $"201-555-{(index % 10_000):0000}";

    private static string NormalizeEmailSlug(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = c;
                continue;
            }

            if (length > 0 && buffer[length - 1] != '.')
                buffer[length++] = '.';
        }

        var slug = new string(buffer[..length]).Trim('.');
        return string.IsNullOrWhiteSpace(slug) ? "agency.demo" : slug;
    }

    private static string BuildCompanyName(IReadOnlyList<string> prefixes, IReadOnlyList<string> suffixes, int index)
    {
        var prefix = prefixes[index % prefixes.Count];
        var suffix = suffixes[(index * 7 + (index / Math.Max(1, prefixes.Count))) % suffixes.Count];

        if (index < prefixes.Count * suffixes.Count)
            return $"{prefix} {suffix}";

        return $"{prefix} {suffix} {index + 1}";
    }

    private static string BuildPersonName(int index)
    {
        var first = FirstNames[index % FirstNames.Length];
        var last = LastNames[(index * 5 + (index / Math.Max(1, FirstNames.Length))) % LastNames.Length];

        if (index < FirstNames.Length * LastNames.Length)
            return $"{first} {last}";

        return $"{first} {last} {index + 1}";
    }

    private sealed record ServiceItemTemplate(
        string Code,
        string Name,
        AgencyBillingServiceItemUnitOfMeasure UnitOfMeasure);

    private sealed record PaymentTermSeed(string Display, Guid Id, int DueDays);

    private sealed record ClientSeed(Guid Id, string Display, string ClientCode, PaymentTermSeed PaymentTerms);

    private sealed record TeamMemberSeed(
        Guid Id,
        string Display,
        string MemberCode,
        string Title,
        decimal DefaultBillingRate,
        decimal DefaultCostRate);

    private sealed record ServiceItemSeed(
        Guid Id,
        string Code,
        string Display,
        AgencyBillingServiceItemUnitOfMeasure UnitOfMeasure);

    private sealed record ProjectAssignmentSeed(
        TeamMemberSeed TeamMember,
        ServiceItemSeed ServiceItem,
        decimal BillingRate,
        decimal CostRate);

    private sealed record ProjectSeed(
        Guid Id,
        string Display,
        string ProjectCode,
        ClientSeed Client,
        TeamMemberSeed Manager,
        IReadOnlyList<ProjectAssignmentSeed> Assignments,
        DateOnly StartDate);

    private sealed record ContractSeed(Guid Id, ProjectSeed Project, DateOnly EffectiveFrom);

    private sealed record TimesheetPlan(
        ContractSeed Contract,
        ProjectAssignmentSeed Assignment,
        DateOnly WorkDate,
        decimal Hours,
        decimal Amount,
        decimal CostAmount,
        string Description);

    private sealed record TimesheetSeed(
        Guid Id,
        ContractSeed Contract,
        ProjectAssignmentSeed Assignment,
        DateOnly WorkDate,
        decimal Hours,
        decimal Amount,
        string Description);

    private sealed record SalesInvoiceSeed(
        Guid Id,
        ContractSeed Contract,
        DateOnly InvoiceDate,
        DateOnly DueDate,
        decimal Amount);

    private sealed record CustomerPaymentSeed(Guid Id);
}
