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
public sealed class ChartOfAccounts_ValidationContracts_Http_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChartOfAccounts_ValidationContracts_Http_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenCashFlowRoleRequiresLineCode_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(
                Code: UniqueCode("11"),
                Name: "Working Capital Without Line",
                AccountType: "Asset",
                IsActive: true,
                CashFlowRole: "WorkingCapital",
                CashFlowLineCode: null));

        await AssertInvalidArgumentAsync(
            response,
            expectedParamName: "CashFlowLineCode",
            expectedMessageFragment: "required when CashFlowRole is 'WorkingCapital'");
    }

    [Fact]
    public async Task Create_WhenCashEquivalentProvidesLineCode_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(
                Code: UniqueCode("10"),
                Name: "Cash Equivalent With Line",
                AccountType: "Asset",
                IsActive: true,
                CashFlowRole: "CashEquivalent",
                CashFlowLineCode: "op_wc_accounts_receivable"));

        await AssertInvalidArgumentAsync(
            response,
            expectedParamName: "CashFlowLineCode",
            expectedMessageFragment: "not allowed when CashFlowRole is 'CashEquivalent'");
    }

    [Fact]
    public async Task Create_WhenCashFlowLineCodeIsUnknown_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(
                Code: UniqueCode("12"),
                Name: "Unknown Cash Flow Line",
                AccountType: "Asset",
                IsActive: true,
                CashFlowRole: "WorkingCapital",
                CashFlowLineCode: "no_such_cash_flow_line"));

        await AssertInvalidArgumentAsync(
            response,
            expectedParamName: "CashFlowLineCode",
            expectedMessageFragment: "Unknown cash flow line code 'no_such_cash_flow_line'");
    }

    [Fact]
    public async Task Update_WhenRoleSectionCombinationIsInvalid_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var created = await CreateAsync(
            client,
            new ChartOfAccountsUpsertRequestDto(
                Code: UniqueCode("20"),
                Name: "Liability For Invalid Role Update",
                AccountType: "Liability",
                IsActive: true));

        using var response = await client.PutAsJsonAsync(
            $"/api/chart-of-accounts/{created.AccountId}",
            new ChartOfAccountsUpsertRequestDto(
                Code: created.Code,
                Name: created.Name,
                AccountType: "Liability",
                IsActive: true,
                CashFlowRole: "InvestingCounterparty",
                CashFlowLineCode: "inv_property_equipment_net"));

        await AssertInvalidArgumentAsync(
            response,
            expectedParamName: "CashFlowRole",
            expectedMessageFragment: "Investing counterparty accounts must belong to Assets");
    }

    private static HttpClient CreateClient(PmApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<ChartOfAccountsAccountDto> CreateAsync(HttpClient client, ChartOfAccountsUpsertRequestDto request)
    {
        using var response = await client.PostAsJsonAsync("/api/chart-of-accounts", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static async Task AssertInvalidArgumentAsync(
        HttpResponseMessage response,
        string expectedParamName,
        string expectedMessageFragment)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var root = await ReadJsonAsync(response);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        root.GetProperty("detail").GetString().Should().Contain(expectedMessageFragment);
        root.GetProperty("error").GetProperty("context").GetProperty("paramName").GetString().Should().Be(expectedParamName);
        root.GetProperty("error").GetProperty("errors").GetProperty(expectedParamName)[0].GetString().Should().Contain(expectedMessageFragment);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static string UniqueCode(string prefix)
        => $"{prefix}-{Guid.CreateVersion7():N}"[..12];
}
