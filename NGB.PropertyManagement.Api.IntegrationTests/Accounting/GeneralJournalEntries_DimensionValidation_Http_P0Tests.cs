using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Contracts.Accounting;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class GeneralJournalEntries_DimensionValidation_Http_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public GeneralJournalEntries_DimensionValidation_Http_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReplaceLines_WhenRequiredDimensionIsMissing_Returns_ValidationProblem_AndDoesNotPersistLines()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var requiredAccountId = await CreateRequiredDimensionAccountAsync(factory, "Required Dimension Account");
        using var client = CreateClient(factory);
        var cash = await GetAccountAsync(client, "1000");

        var draftId = await CreateDraftAsync(client);

        using var response = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{draftId}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, requiredAccountId, 10m, "Debit missing dims"),
                    new GeneralJournalEntryLineInputDto(2, cash.AccountId, 10m, "Credit cash")
                ]));

        await AssertDimensionProblemAsync(
            response,
            expectedReason: "missing_required_dimensions",
            expectedLineNo: 1,
            expectedAccountId: requiredAccountId);

        await AssertDraftHasNoLinesAsync(client, draftId);
    }

    [Fact]
    public async Task ReplaceLines_WhenDimensionsAreNotAllowed_Returns_ValidationProblem_AndDoesNotPersistLines()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var requiredAccountId = await CreateRequiredDimensionAccountAsync(factory, "Dimension Source Account");
        var offsetAccountId = await CreatePlainOffsetAccountAsync(factory, "Offset Revenue");
        using var client = CreateClient(factory);

        var cash = await GetAccountAsync(client, "1000");
        var dimRule = (await GetAccountContextAsync(client, requiredAccountId)).DimensionRules.Single();
        var draftId = await CreateDraftAsync(client);

        using var response = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{draftId}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(
                        1,
                        cash.AccountId,
                        10m,
                        "Debit cash with forbidden dims",
                        [new GeneralJournalEntryDimensionValueDto(dimRule.DimensionId, Guid.CreateVersion7())]),
                    new GeneralJournalEntryLineInputDto(2, offsetAccountId, 10m, "Credit offset")
                ]));

        await AssertDimensionProblemAsync(
            response,
            expectedReason: "dimensions_not_allowed",
            expectedLineNo: 1,
            expectedAccountId: cash.AccountId);

        await AssertDraftHasNoLinesAsync(client, draftId);
    }

    [Fact]
    public async Task ReplaceLines_WhenDimensionValuesConflict_Returns_ValidationProblem_AndDoesNotPersistLines()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var requiredAccountId = await CreateRequiredDimensionAccountAsync(factory, "Conflicting Dimension Account");
        using var client = CreateClient(factory);
        var cash = await GetAccountAsync(client, "1000");

        var dimRule = (await GetAccountContextAsync(client, requiredAccountId)).DimensionRules.Single();
        var valueA = Guid.CreateVersion7();
        var valueB = Guid.CreateVersion7();
        var draftId = await CreateDraftAsync(client);

        using var response = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{draftId}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(
                        1,
                        requiredAccountId,
                        10m,
                        "Debit conflicting dims",
                        [
                            new GeneralJournalEntryDimensionValueDto(dimRule.DimensionId, valueA),
                            new GeneralJournalEntryDimensionValueDto(dimRule.DimensionId, valueB)
                        ]),
                    new GeneralJournalEntryLineInputDto(2, cash.AccountId, 10m, "Credit cash")
                ]));

        await AssertDimensionProblemAsync(
            response,
            expectedReason: "conflicting_dimension_values",
            expectedLineNo: 1,
            expectedAccountId: requiredAccountId);

        await AssertDraftHasNoLinesAsync(client, draftId);
    }

    private static HttpClient CreateClient(PmApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task EnsurePmDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateRequiredDimensionAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"41{Guid.CreateVersion7():N}"[..10],
                Name: name,
                Type: AccountType.Expense,
                StatementSection: StatementSection.Expenses,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("building", IsRequired: true, Ordinal: 1)
                ]),
            CancellationToken.None);
    }

    private static async Task<Guid> CreatePlainOffsetAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"39{Guid.CreateVersion7():N}"[..10],
                Name: name,
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity),
            CancellationToken.None);
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    private static async Task<GeneralJournalEntryAccountContextDto> GetAccountContextAsync(HttpClient client, Guid accountId)
    {
        using var response = await client.GetAsync($"/api/accounting/general-journal-entries/accounts/{accountId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<GeneralJournalEntryAccountContextDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        dto.Should().NotBeNull();
        return dto!.Document.Id;
    }

    private static async Task AssertDraftHasNoLinesAsync(HttpClient client, Guid draftId)
    {
        var details = await client.GetFromJsonAsync<GeneralJournalEntryDetailsDto>($"/api/accounting/general-journal-entries/{draftId}", Json);
        details.Should().NotBeNull();
        details!.Lines.Should().BeEmpty();
    }

    private static async Task AssertDimensionProblemAsync(
        HttpResponseMessage response,
        string expectedReason,
        int expectedLineNo,
        Guid expectedAccountId)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var root = await ReadJsonAsync(response);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("gje.line.dimensions.invalid");

        var context = root.GetProperty("error").GetProperty("context");
        context.GetProperty("reason").GetString().Should().Be(expectedReason);
        context.GetProperty("lineNo").GetInt32().Should().Be(expectedLineNo);
        context.GetProperty("accountId").GetGuid().Should().Be(expectedAccountId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
