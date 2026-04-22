using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Api.Models;
using NGB.Accounting.Documents;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMainMenu_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmMainMenu_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MainMenu_Returns_DomainDriven_PropertyManagement_IA()
    {
        using var factory = new PmApiFactory(_fixture, new Dictionary<string, string?>
        {
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.HealthUiUrl)}"] = "https://localhost:7075/health-ui",
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.BackgroundJobsUiUrl)}"] = "https://localhost:7074/hangfire"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var resp = await client.GetAsync("/api/main-menu");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = await resp.Content.ReadFromJsonAsync<MainMenuDto>(Json);
        menu.Should().NotBeNull();

        var groups = menu!.Groups;
        groups.Select(g => g.Label).Should().Equal(
            "Dashboard",
            "Portfolio",
            "Receivables",
            "Payables",
            "Maintenance",
            "Accounting",
            "Setup & Controls");

        var labels = groups.Select(g => g.Label).ToArray();
        labels.Should().NotContain("Start");
        labels.Should().NotContain("Catalogs");
        labels.Should().NotContain("Documents");
        labels.Should().NotContain("Reports");
        labels.Should().NotContain("Admin");

        var dashboardGroup = groups.Single(g => g.Label == "Dashboard");
        dashboardGroup.Icon.Should().Be("home");
        dashboardGroup.Items.Should().ContainSingle(i =>
            i.Code == "pm.home"
            && i.Label == "Dashboard"
            && i.Route == "/home"
            && i.Icon == "home");

        var portfolioGroup = groups.Single(g => g.Label == "Portfolio");
        portfolioGroup.Icon.Should().Be("building-2");
        portfolioGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.Property && i.Label == "Properties & Units" && i.Route == "/catalogs/pm.property" && i.Icon == "building-2");
        portfolioGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.Party && i.Label == "Parties" && i.Route == "/catalogs/pm.party" && i.Icon == "users");
        portfolioGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.Lease && i.Label == "Leases" && i.Route == "/documents/pm.lease");
        portfolioGroup.Items.Should().Contain(i => i.Code == "pm.building.summary" && i.Label == "Building Summary");
        portfolioGroup.Items.Should().Contain(i => i.Code == "pm.occupancy.summary" && i.Label == "Occupancy Summary");

        var receivablesGroup = groups.Single(g => g.Label == "Receivables");
        receivablesGroup.Icon.Should().Be("coins");
        receivablesGroup.Items.Select(i => i.Label).Should().ContainInOrder(
            "Open Items",
            "Reconciliation",
            "Rent Charges",
            "Other Charges",
            "Late Fees",
            "Payments",
            "Returned Payments",
            "Credit Memos",
            "Allocations",
            "Tenant Statement",
            "Aging",
            "Open Items Report",
            "Open Items Detail");
        receivablesGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.ReceivableApply && i.Label == "Allocations" && i.Icon == "git-merge");
        receivablesGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.ReceivableCharge && i.Label == "Other Charges");
        receivablesGroup.Items.Should().Contain(i => i.Code == "pm.receivables.reconciliation" && i.Icon == "scale");

        var payablesGroup = groups.Single(g => g.Label == "Payables");
        payablesGroup.Icon.Should().Be("wallet");
        payablesGroup.Items.Select(i => i.Label).Should().ContainInOrder(
            "Open Items",
            "Charges",
            "Payments",
            "Credit Memos",
            "Allocations");
        payablesGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.PayableApply && i.Label == "Allocations");

        var maintenanceGroup = groups.Single(g => g.Label == "Maintenance");
        maintenanceGroup.Icon.Should().Be("wrench");
        maintenanceGroup.Items.Select(i => i.Label).Should().ContainInOrder(
            "Requests",
            "Work Orders",
            "Completions",
            "Open Queue",
            "Categories");
        maintenanceGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.MaintenanceCategory && i.Label == "Categories");
        maintenanceGroup.Items.Should().Contain(i => i.Code == "pm.maintenance.queue" && i.Label == "Open Queue" && i.Icon == "bar-chart");

        var accountingGroup = groups.Single(g => g.Label == "Accounting");
        accountingGroup.Icon.Should().Be("calculator");
        accountingGroup.Items.Select(i => i.Label).Should().ContainInOrder(
            "Journal Entries",
            "Trial Balance",
            "Balance Sheet",
            "Income Statement",
            "Statement of Changes in Equity",
            "Cash Flow Statement",
            "General Journal",
            "Account Card",
            "General Ledger",
            "Ledger Analysis");
        accountingGroup.Items.Should().Contain(i =>
            i.Code == AccountingDocumentTypeCodes.GeneralJournalEntry
            && i.Label == "Journal Entries"
            && i.Route == "/documents/accounting.general_journal_entry"
            && i.Icon == "book-open");
        accountingGroup.Items.Should().Contain(i => i.Code == "accounting.cash_flow_statement_indirect" && i.Label == "Cash Flow Statement");
        accountingGroup.Items.Should().Contain(i => i.Code == "accounting.general_ledger_aggregated" && i.Label == "General Ledger");

        var setupGroup = groups.Single(g => g.Label == "Setup & Controls");
        setupGroup.Icon.Should().Be("settings");
        setupGroup.Items.Select(i => i.Label).Should().ContainInOrder(
            "Accounting Policy",
            "Chart of Accounts",
            "Bank Accounts",
            "Receivable Charge Types",
            "Payable Charge Types",
            "Period Close",
            "Posting Log",
            "Integrity Checks",
            "Health",
            "Background Jobs");
        setupGroup.Items.Should().ContainSingle(i => i.Code == PropertyManagementCodes.AccountingPolicy && i.Route == "/catalogs/pm.accounting_policy");
        setupGroup.Items.Should().Contain(i => i.Code == "chart-of-accounts" && i.Route == "/admin/chart-of-accounts" && i.Label == "Chart of Accounts");
        setupGroup.Items.Should().Contain(i => i.Code == "accounting.period_closing" && i.Route == "/admin/accounting/period-closing" && i.Label == "Period Close");
        setupGroup.Items.Should().Contain(i => i.Code == "accounting.consistency" && i.Route == "/admin/accounting/consistency" && i.Label == "Integrity Checks" && i.Icon == "shield-check");
        setupGroup.Items.Should().Contain(i => i.Code == "pm.health" && i.Route == "https://localhost:7075/health-ui" && i.Label == "Health" && i.Icon == "heart-pulse");
        setupGroup.Items.Should().Contain(i => i.Code == PropertyManagementCodes.BackgroundJobs && i.Route == "https://localhost:7074/hangfire" && i.Label == "Background Jobs" && i.Icon == "cogs");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
