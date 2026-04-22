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
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class PeriodClosing_FiscalYear_Dimensions_Http_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PeriodClosing_FiscalYear_Dimensions_Http_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FiscalYearClose_WithHttpPostedDimensionedMovements_PreservesDistinctDimensionSets()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var retainedEarningsId = await CreateRetainedEarningsAccountAsync(factory, "Retained Earnings Fiscal Year HTTP");
        var revenueId = await CreateRevenueAccountWithFourDimensionsAsync(factory, "HTTP Dimensioned Revenue");

        using var client = CreateHttpsClient(factory);
        using var reviewerClient = CreateHttpsClient(factory, PmKeycloakTestUsers.Analyst);

        var cash = await GetAccountAsync(client, "1000");
        var revenueContext = await GetAccountContextAsync(client, revenueId);

        revenueContext.DimensionRules.Select(x => x.Ordinal).Should().Equal(10, 20, 30, 40);

        var dim1 = revenueContext.DimensionRules[0].DimensionId;
        var dim2 = revenueContext.DimensionRules[1].DimensionId;
        var dim3 = revenueContext.DimensionRules[2].DimensionId;
        var dim4 = revenueContext.DimensionRules[3].DimensionId;

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();
        var v4a = Guid.CreateVersion7();
        var v4b = Guid.CreateVersion7();

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            dateUtc: new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc),
            debitAccountId: cash.AccountId,
            creditAccountId: revenueId,
            amount: 100m,
            creditDimensions:
            [
                new GeneralJournalEntryDimensionValueDto(dim1, v1),
                new GeneralJournalEntryDimensionValueDto(dim2, v2),
                new GeneralJournalEntryDimensionValueDto(dim3, v3),
                new GeneralJournalEntryDimensionValueDto(dim4, v4a)
            ]);

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            dateUtc: new DateTime(2026, 1, 14, 12, 0, 0, DateTimeKind.Utc),
            debitAccountId: cash.AccountId,
            creditAccountId: revenueId,
            amount: 200m,
            creditDimensions:
            [
                new GeneralJournalEntryDimensionValueDto(dim1, v1),
                new GeneralJournalEntryDimensionValueDto(dim2, v2),
                new GeneralJournalEntryDimensionValueDto(dim3, v3),
                new GeneralJournalEntryDimensionValueDto(dim4, v4b)
            ]);

        using var closeResponse = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(
                FiscalYearEndPeriod: new DateOnly(2026, 1, 1),
                RetainedEarningsAccountId: retainedEarningsId));

        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var closeStatus = await closeResponse.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        closeStatus.Should().NotBeNull();
        closeStatus!.State.Should().Be("Completed");

        var expectedCloseDocumentId = DeterministicGuid.Create("CloseFiscalYear|2026-01-01");

        await using var scope = factory.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);

        var closingEntries = entries
            .Where(x => x.Debit.Id == revenueId && x.Credit.Id == retainedEarningsId)
            .ToList();

        closingEntries.Should().HaveCount(2);
        closingEntries.Select(x => x.Amount).OrderBy(x => x).Should().Equal(100m, 200m);
        closingEntries.All(x => x.CreditDimensionSetId == Guid.Empty).Should().BeTrue();

        var setIds = closingEntries.Select(x => x.DebitDimensionSetId).Distinct().ToArray();
        setIds.Should().HaveCount(2);
        setIds.Should().NotContain(Guid.Empty);

        var bagsById = await scope.ServiceProvider.GetRequiredService<IDimensionSetReader>()
            .GetBagsByIdsAsync(setIds, CancellationToken.None);

        bagsById.Values.Select(x => x.Items.Single(i => i.DimensionId == dim4).ValueId)
            .Should().BeEquivalentTo(new[] { v4a, v4b });

        bagsById.Values.All(x =>
                x.Items.Single(i => i.DimensionId == dim1).ValueId == v1 &&
                x.Items.Single(i => i.DimensionId == dim2).ValueId == v2 &&
                x.Items.Single(i => i.DimensionId == dim3).ValueId == v3)
            .Should().BeTrue();
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            },
            user: user);

    private static async Task EnsurePmDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateRetainedEarningsAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"RE-{Guid.CreateVersion7():N}"[..12],
                Name: name,
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                IsActive: true),
            CancellationToken.None);
    }

    private static async Task<Guid> CreateRevenueAccountWithFourDimensionsAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"49{Guid.CreateVersion7():N}"[..10],
                Name: name,
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("buildings", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest("counterparties", IsRequired: true, Ordinal: 20),
                    new AccountDimensionRuleRequest("contracts", IsRequired: false, Ordinal: 30),
                    new AccountDimensionRuleRequest("floors", IsRequired: false, Ordinal: 40)
                ]),
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

    private static async Task CreateAndPostGeneralJournalEntryAsync(
        HttpClient submitterClient,
        HttpClient approverClient,
        DateTime dateUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount,
        IReadOnlyList<GeneralJournalEntryDimensionValueDto> creditDimensions)
    {
        var createResponse = await submitterClient.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(dateUtc));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();

        var id = created!.Document.Id;

        var headerResponse = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "author",
                JournalType: 1,
                ReasonCode: "PERIOD",
                Memo: "Fiscal year close dimensions over HTTP",
                ExternalReference: $"FYDIM-{id:N}"[..16],
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResponse = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, debitAccountId, amount, "Debit cash"),
                    new GeneralJournalEntryLineInputDto(2, creditAccountId, amount, "Credit revenue", creditDimensions)
                ]));
        linesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResponse = await submitterClient.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveResponse = await approverClient.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var postResponse = await approverClient.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/post", new { });
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
