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
public sealed class PeriodClosing_MonthStatus_RouteContracts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PeriodClosing_MonthStatus_RouteContracts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MonthStatus_Rejects_Anonymous_Request()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAnonymousClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/accounting/period-closing/month?period=2026-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MonthStatus_Reflects_Activity_And_Close_State_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var revenueId = await CreateRevenueAccountAsync(factory, "Route Period Close Revenue");
        using var client = CreateHttpsClient(factory);
        using var reviewerClient = CreateHttpsClient(factory, PmKeycloakTestUsers.Analyst);
        var cash = await GetAccountAsync(client, "1000");

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            cash.AccountId,
            revenueId,
            175m,
            new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc));

        using (var beforeResponse = await client.GetAsync("/api/accounting/period-closing/month?period=2026-01-01"))
        {
            beforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var before = await beforeResponse.Content.ReadFromJsonAsync<PeriodCloseStatusDto>(Json);
            before.Should().NotBeNull();
            before!.Period.Should().Be(new DateOnly(2026, 1, 1));
            before.State.Should().Be("ReadyToClose");
            before.HasActivity.Should().BeTrue();
            before.IsClosed.Should().BeFalse();
            before.CanClose.Should().BeTrue();
        }

        var closeResponse = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(new DateOnly(2026, 1, 1)));
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await closeResponse.Content.ReadFromJsonAsync<PeriodCloseStatusDto>(Json);
        closed.Should().NotBeNull();
        closed!.State.Should().Be("Closed");
        closed.IsClosed.Should().BeTrue();
        closed.CanReopen.Should().BeTrue();
        closed.ClosedBy.Should().Be("PM Admin");

        using var afterResponse = await client.GetAsync("/api/accounting/period-closing/month?period=2026-01-01");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await afterResponse.Content.ReadFromJsonAsync<PeriodCloseStatusDto>(Json);
        after.Should().NotBeNull();
        after!.State.Should().Be("Closed");
        after.IsClosed.Should().BeTrue();
        after.CanClose.Should().BeFalse();
        after.CanReopen.Should().BeTrue();
    }

    private static async Task EnsurePmDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => user is null
            ? factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") })
            : factory.CreateAuthenticatedClient(
                new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
                user: user);

    private static async Task<Guid> CreateRevenueAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"REV-{Guid.CreateVersion7():N}"[..12],
                Name: name,
                Type: AccountType.Income,
                StatementSection: StatementSection.Income),
            CancellationToken.None);
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    private static async Task CreateAndPostGeneralJournalEntryAsync(
        HttpClient client,
        HttpClient reviewerClient,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount,
        DateTime dateUtc)
    {
        var createResponse = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(dateUtc));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();

        var id = created!.Document.Id;

        var headerResponse = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "author",
                JournalType: 1,
                ReasonCode: "PERIOD",
                Memo: "Period closing route contract",
                ExternalReference: "PERIOD-001",
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResponse = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, debitAccountId, amount, "Debit"),
                    new GeneralJournalEntryLineInputDto(2, creditAccountId, amount, "Credit")
                ]));
        linesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResponse = await client.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveResponse = await reviewerClient.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var postResponse = await reviewerClient.PostAsJsonAsync($"/api/accounting/general-journal-entries/{id}/post", new { });
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
