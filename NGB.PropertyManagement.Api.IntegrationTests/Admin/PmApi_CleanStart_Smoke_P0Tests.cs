using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NGB.Accounting.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Admin;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Migrator.Seed;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmApi_CleanStart_Smoke_P0Tests(PmIntegrationFixture fixture)
{
    [Fact]
    public async Task Api_AfterCleanPackMigrate_AndSeedDefaults_CanReadMainMenuCoaAndSeededCatalogs()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_api_clean");

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(db.ConnectionString);

        var seedExit = await PropertyManagementSeedDefaultsCli.RunAsync(["--connection", db.ConnectionString]);
        seedExit.Should().Be(0);

        using var factory = new PmApiFactory(fixture, db.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using (var menuResp = await client.GetAsync("/api/main-menu"))
        {
            menuResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var menu = await menuResp.Content.ReadFromJsonAsync<MainMenuDto>();
            menu.Should().NotBeNull();
            menu!.Groups.Select(g => g.Label).Should().ContainInOrder(
                "Dashboard",
                "Portfolio",
                "Receivables",
                "Payables",
                "Maintenance",
                "Accounting",
                "Setup & Controls");
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.Party);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.Property);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.AccountingPolicy);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.PayableChargeType);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.PayableCharge);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(PropertyManagementCodes.PayablePayment);
            menu.Groups.SelectMany(g => g.Items).Select(i => i.Code).Should().Contain(AccountingDocumentTypeCodes.GeneralJournalEntry);
        }

        using (var coaResp = await client.GetAsync("/api/chart-of-accounts?search=1100&limit=20"))
        {
            coaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await coaResp.Content.ReadFromJsonAsync<ChartOfAccountsPageDto>();
            page.Should().NotBeNull();
            page!.Items.Should().ContainSingle(x => x.Code == "1100" && x.Name == "Accounts Receivable - Tenants");
        }

        using (var policyResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.AccountingPolicy}?offset=0&limit=10"))
        {
            policyResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await policyResp.Content.ReadFromJsonAsync<PageResponseDto<CatalogItemDto>>();
            page.Should().NotBeNull();
            page!.Items.Should().ContainSingle();
            page.Items[0].Display.Should().Be("Property Management - Accounting Policy");
        }

        using (var chargeTypeResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.ReceivableChargeType}?offset=0&limit=10"))
        {
            chargeTypeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await chargeTypeResp.Content.ReadFromJsonAsync<PageResponseDto<CatalogItemDto>>();
            page.Should().NotBeNull();
            page!.Items.Select(x => x.Display).Should().BeEquivalentTo(
            [
                "Rent",
                "Late Fee",
                "Utility",
                "Parking",
                "Damage",
                "Move out",
                "Misc"
            ]);
        }

        using (var payableChargeTypeResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.PayableChargeType}?offset=0&limit=10"))
        {
            payableChargeTypeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await payableChargeTypeResp.Content.ReadFromJsonAsync<PageResponseDto<CatalogItemDto>>();
            page.Should().NotBeNull();
            page!.Items.Select(x => x.Display).Should().BeEquivalentTo(
            [
                "Repair",
                "Utility",
                "Cleaning",
                "Supply",
                "Misc"
            ]);
        }
    }
}
