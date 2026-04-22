using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class ChartOfAccounts_HttpFilters_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChartOfAccounts_HttpFilters_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Page_Default_Excludes_Deleted_But_Includes_Inactive()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        // IMPORTANT:
        // Guid v7 starts with a timestamp. Taking only the first few hex chars is NOT unique
        // within a short window (~65s for the first 8 chars). Use the random tail.
        var tag = UniqueTag();
        var c1000 = $"UT{tag}-1000";
        var c2000 = $"UT{tag}-2000";
        var c4000 = $"UT{tag}-4000";
        var c6000 = $"UT{tag}-6000";

        var a1 = await CreateAsync(client, c1000, "Cash", "Asset", isActive: true);
        var a2 = await CreateAsync(client, c2000, "Accounts Payable", "Liability", isActive: false);
        var a3 = await CreateAsync(client, c4000, "Rent Income", "Income", isActive: true);
        var a4 = await CreateAsync(client, c6000, "Maintenance", "Expense", isActive: true);

        await MarkForDeletionAsync(client, a4.AccountId);

        // Narrow by search to avoid seeded/system accounts.
        var page = await GetPageAsync(client, $"/api/chart-of-accounts?search={tag}");

        page.Total.Should().Be(3);
        page.Items.Select(x => x.Code).Should().BeEquivalentTo([c1000, c2000, c4000]);
        page.Items.Single(x => x.Code == c2000).IsActive.Should().BeFalse();
        page.Items.Should().OnlyContain(x => x.IsDeleted == false);
    }

    [Fact]
    public async Task Page_OnlyActive_Filters_To_Active_Accounts()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var c1000 = $"UT{tag}-1000";
        var c2000 = $"UT{tag}-2000";
        var c4000 = $"UT{tag}-4000";

        await CreateAsync(client, c1000, "Cash", "Asset", isActive: true);
        await CreateAsync(client, c2000, "Accounts Payable", "Liability", isActive: false);
        await CreateAsync(client, c4000, "Rent Income", "Income", isActive: true);

        var page = await GetPageAsync(client, $"/api/chart-of-accounts?onlyActive=true&search={tag}");

        page.Total.Should().Be(2);
        page.Items.Select(x => x.Code).Should().BeEquivalentTo([c1000, c4000]);
    }

    [Fact]
    public async Task Page_OnlyInactive_Filters_To_Inactive_Accounts()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var c1000 = $"UT{tag}-1000";
        var c2000 = $"UT{tag}-2000";
        var c4000 = $"UT{tag}-4000";

        await CreateAsync(client, c1000, "Cash", "Asset", isActive: true);
        await CreateAsync(client, c2000, "Accounts Payable", "Liability", isActive: false);
        await CreateAsync(client, c4000, "Rent Income", "Income", isActive: true);

        var page = await GetPageAsync(client, $"/api/chart-of-accounts?onlyActive=false&search={tag}");

        page.Total.Should().Be(1);
        page.Items.Select(x => x.Code).Should().BeEquivalentTo([c2000]);
    }

    [Fact]
    public async Task Page_IncludeDeleted_Includes_SoftDeleted_Accounts()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var c1000 = $"UT{tag}-1000";
        var c6000 = $"UT{tag}-6000";

        var a1 = await CreateAsync(client, c1000, "Cash", "Asset", isActive: true);
        var a2 = await CreateAsync(client, c6000, "Maintenance", "Expense", isActive: true);

        await MarkForDeletionAsync(client, a2.AccountId);

        var page = await GetPageAsync(client, $"/api/chart-of-accounts?includeDeleted=true&search={tag}");

        page.Total.Should().Be(2);
        page.Items.Select(x => x.Code).Should().BeEquivalentTo([c1000, c6000]);
        page.Items.Single(x => x.Code == c6000).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Page_AccountTypes_Filter_Works_With_RepeatedQueryParams()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var c1000 = $"UT{tag}-1000";
        var c2000 = $"UT{tag}-2000";
        var c4000 = $"UT{tag}-4000";

        await CreateAsync(client, c1000, "Cash", "Asset", isActive: true);
        await CreateAsync(client, c2000, "Accounts Payable", "Liability", isActive: true);
        await CreateAsync(client, c4000, "Rent Income", "Income", isActive: true);

        var page = await GetPageAsync(client, $"/api/chart-of-accounts?accountTypes=Asset&accountTypes=Income&search={tag}");

        page.Total.Should().Be(2);
        page.Items.Select(x => x.Code).Should().BeEquivalentTo([c1000, c4000]);
    }

    [Fact]
    public async Task Mark_Unmark_And_SetActive_Affect_GetById()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var c7000 = $"UT{tag}-7000";

        var acc = await CreateAsync(client, c7000, "Parking Income", "Income", isActive: true);

        // Toggle active.
        var setActiveResp = await client.PostAsync($"/api/chart-of-accounts/{acc.AccountId}/set-active?isActive=false", content: null);
        setActiveResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeactivate = await GetByIdAsync(client, acc.AccountId);
        afterDeactivate.IsActive.Should().BeFalse();

        // Mark/unmark for deletion.
        await MarkForDeletionAsync(client, acc.AccountId);
        (await GetByIdAsync(client, acc.AccountId)).IsDeleted.Should().BeTrue();

        var unmarkResp = await client.PostAsync($"/api/chart-of-accounts/{acc.AccountId}/unmark-for-deletion", content: null);
        unmarkResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetByIdAsync(client, acc.AccountId)).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Metadata_Returns_BackendOwned_AccountType_Role_And_Line_Options()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var resp = await client.GetAsync("/api/chart-of-accounts/metadata");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await resp.Content.ReadFromJsonAsync<ChartOfAccountsMetadataDto>(Json);
        metadata.Should().NotBeNull();

        metadata!.AccountTypeOptions.Select(x => x.Value)
            .Should().Contain(["Asset", "Liability", "Equity", "Income", "Expense"]);

        metadata.CashFlowRoleOptions.Should().Contain(x =>
            x.Value == string.Empty
            && x.Label == "None"
            && x.SupportsLineCode == false
            && x.RequiresLineCode == false);

        metadata.CashFlowRoleOptions.Should().Contain(x =>
            x.Value == "WorkingCapital"
            && x.SupportsLineCode
            && x.RequiresLineCode);

        metadata.CashFlowLineOptions.Should().Contain(x =>
            x.Value == "op_wc_accounts_receivable"
            && x.AllowedRoles.Contains("WorkingCapital"));

        metadata.CashFlowLineOptions.Should().Contain(x =>
            x.Value == "fin_debt_net"
            && x.AllowedRoles.Contains("FinancingCounterparty"));
    }

    private static HttpClient CreateClient(PmApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<ChartOfAccountsAccountDto> CreateAsync(
        HttpClient client,
        string code,
        string name,
        string accountType,
        bool isActive)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(code, name, accountType, isActive));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        dto.Should().NotBeNull();
        dto!.Code.Should().Be(code);
        dto.IsActive.Should().Be(isActive);
        return dto;
    }

    private static async Task MarkForDeletionAsync(HttpClient client, Guid accountId)
    {
        var resp = await client.PostAsync($"/api/chart-of-accounts/{accountId}/mark-for-deletion", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<ChartOfAccountsAccountDto> GetByIdAsync(HttpClient client, Guid accountId)
    {
        var resp = await client.GetAsync($"/api/chart-of-accounts/{accountId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static async Task<ChartOfAccountsPageDto> GetPageAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await resp.Content.ReadFromJsonAsync<ChartOfAccountsPageDto>(Json);
        page.Should().NotBeNull();
        return page!;
    }

    private static string UniqueTag()
    {
        var n = Guid.CreateVersion7().ToString("N");
        return n[^12..];
    }
}
