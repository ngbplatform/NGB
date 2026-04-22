using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Admin;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class ChartOfAccounts_ByIds_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public ChartOfAccounts_ByIds_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ByIds_Returns_Code_Name_Labels_For_Requested_Accounts()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var a = await CreateAsync(client, $"1100-{UniqueTag()}", "Accounts Receivable - Lookup", "Asset", true);
        var b = await CreateAsync(client, $"4000-{UniqueTag()}", "Rental Income - Lookup", "Income", true);

        using var resp = await client.PostAsJsonAsync("/api/chart-of-accounts/by-ids", new ByIdsRequestDto([a.AccountId, b.AccountId]));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<LookupItemDto>>(Json);
        items.Should().NotBeNull();
        items!.Should().Contain(x => x.Id == a.AccountId && x.Label == $"{a.Code} — {a.Name}");
        items.Should().Contain(x => x.Id == b.AccountId && x.Label == $"{b.Code} — {b.Name}");
    }

    private static async Task<ChartOfAccountsAccountDto> CreateAsync(HttpClient client, string code, string name, string accountType, bool isActive)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(code, name, accountType, isActive));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static string UniqueTag()
    {
        var n = Guid.CreateVersion7().ToString("N");
        return n[^8..];
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
