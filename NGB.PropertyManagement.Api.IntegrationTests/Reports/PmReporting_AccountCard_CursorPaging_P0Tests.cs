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
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_AccountCard_CursorPaging_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_AccountCard_CursorPaging_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AccountCard_CursorPaging_Returns_Stable_Detail_Pages_Without_GrandTotal_Rows()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var revenueId = await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: "4900",
                Name: "Reporting Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income),
            CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var reviewerClient = factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user: PmKeycloakTestUsers.Analyst);
        var cash = await GetAccountAsync(client, "1000");

        await CreateAndPostGeneralJournalEntryAsync(client, reviewerClient, cash.AccountId, revenueId, 10m, new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc));
        await CreateAndPostGeneralJournalEntryAsync(client, reviewerClient, cash.AccountId, revenueId, 20m, new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc));
        await CreateAndPostGeneralJournalEntryAsync(client, reviewerClient, cash.AccountId, revenueId, 30m, new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        var page1 = await ExecuteAsync(client, cash.AccountId, null);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNullOrWhiteSpace();
        page1.Sheet.Rows.Should().HaveCount(1);
        page1.Sheet.Rows[0].RowKind.Should().Be(ReportRowKind.Detail);

        var page2 = await ExecuteAsync(client, cash.AccountId, page1.NextCursor);
        page2.HasMore.Should().BeTrue();
        page2.NextCursor.Should().NotBeNullOrWhiteSpace();
        page2.Sheet.Rows.Should().HaveCount(1);
        page2.Sheet.Rows[0].RowKind.Should().Be(ReportRowKind.Detail);

        var page3 = await ExecuteAsync(client, cash.AccountId, page2.NextCursor);
        page3.HasMore.Should().BeFalse();
        page3.NextCursor.Should().BeNull();
        page3.Sheet.Rows.Should().HaveCount(1);
        page3.Sheet.Rows.Select(x => x.RowKind).Should().Equal(ReportRowKind.Detail);

        var documents = new[] { page1, page2, page3 }
            .Select(r => r.Sheet.Rows.First(x => x.RowKind == ReportRowKind.Detail).Cells[2].Display)
            .ToArray();
        documents.Should().OnlyHaveUniqueItems();
    }

    private static async Task<ReportExecutionResponseDto> ExecuteAsync(HttpClient client, Guid accountId, string? cursor)
    {
        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.account_card/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(accountId))
                },
                Layout: new ReportLayoutDto(ShowGrandTotals: false),
                Limit: 1,
                Cursor: cursor));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        dto!.Diagnostics!["executor"].Should().Be("canonical-account-card");
        return dto;
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        var account = page!.Items.Single(x => x.Code == code);
        return account;
    }

    private static async Task CreateAndPostGeneralJournalEntryAsync(HttpClient submitterClient, HttpClient approverClient, Guid debitAccountId, Guid creditAccountId, decimal amount, DateTime dateUtc)
    {
        var createResp = await submitterClient.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(
                DateUtc: dateUtc));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();
        var id = created!.Document.Id;

        var headerResp = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "reporting-tests",
                JournalType: 1,
                ReasonCode: "REPORTING",
                Memo: "Account card paging",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResp = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "reporting-tests",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, debitAccountId, amount, "Debit"),
                    new GeneralJournalEntryLineInputDto(2, creditAccountId, amount, "Credit")
                ]));
        linesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResp = await submitterClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveResp = await approverClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var postResp = await approverClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/post", new { });
        postResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
