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
public sealed class PmReporting_CanonicalAccounting_Drilldown_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_CanonicalAccounting_Drilldown_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Canonical_Accounting_Reports_Expose_AccountCard_Drilldown_And_TrialBalanceStyle_Statement_Shape()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var reviewerClient = factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user: PmKeycloakTestUsers.Analyst);

        var cash = await GetAccountAsync(client, "1000");
        var reportingRevenueId = await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: "4900",
                Name: "Reporting Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income),
            CancellationToken.None);
        const string reportingRevenueDisplay = "4900 — Reporting Revenue";

        await CreateAndPostGeneralJournalEntryAsync(client, reviewerClient, cash.AccountId, reportingRevenueId, 25m);

        var trialBalance = await ExecuteAsync(
            client,
            "accounting.trial_balance",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));
        trialBalance.Sheet.Columns.Select(x => x.Code).Should().Equal("account", "debit_amount", "credit_amount");
        var tbCash = trialBalance.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "1000 — Operating Cash");
        AssertAccountCardAction(tbCash.Cells[0].Action, cash.AccountId, "2026-03-01", "2026-03-31");

        var balanceSheet = await ExecuteAsync(
            client,
            "accounting.balance_sheet",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));
        balanceSheet.Sheet.Columns.Select(x => x.Code).Should().Equal("account", "amount");
        balanceSheet.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.Cells[0].Display == "Assets");
        var bsCash = balanceSheet.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == "1000 — Operating Cash");
        bsCash.OutlineLevel.Should().Be(1);
        AssertAccountCardAction(bsCash.Cells[0].Action, cash.AccountId, "2026-03-01", "2026-03-31");

        var incomeStatement = await ExecuteAsync(
            client,
            "accounting.income_statement",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));
        incomeStatement.Sheet.Columns.Select(x => x.Code).Should().Equal("account", "amount");
        incomeStatement.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.Cells[0].Display == "Income");
        var isIncome = incomeStatement.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail && x.Cells[0].Display == reportingRevenueDisplay);
        isIncome.OutlineLevel.Should().Be(1);
        AssertAccountCardAction(isIncome.Cells[0].Action, reportingRevenueId, "2026-03-01", "2026-03-31");

        var generalJournal = await ExecuteAsync(
            client,
            "accounting.general_journal",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));
        var gjLine = generalJournal.Sheet.Rows.Single(x =>
            x.RowKind == ReportRowKind.Detail &&
            GetCell(generalJournal, x, "debit_account").Display == "1000 — Operating Cash" &&
            GetCell(generalJournal, x, "credit_account").Display == reportingRevenueDisplay);
        AssertAccountCardAction(GetCell(generalJournal, gjLine, "debit_account").Action, cash.AccountId, "2026-03-01", "2026-03-31");
        AssertAccountCardAction(GetCell(generalJournal, gjLine, "credit_account").Action, reportingRevenueId, "2026-03-01", "2026-03-31");

        var accountCard = await ExecuteAsync(
            client,
            "accounting.account_card",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(cash.AccountId))
                },
                Offset: 0,
                Limit: 50));
        var acLine = accountCard.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail && GetCell(accountCard, x, "counter_account").Display == reportingRevenueDisplay);
        AssertAccountCardAction(GetCell(accountCard, acLine, "counter_account").Action, reportingRevenueId, "2026-03-01", "2026-03-31");

        var generalLedger = await ExecuteAsync(
            client,
            "accounting.general_ledger_aggregated",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(cash.AccountId))
                },
                Offset: 0,
                Limit: 50));
        var glLine = generalLedger.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Detail && GetCell(generalLedger, x, "counter_account").Display == reportingRevenueDisplay);
        AssertAccountCardAction(GetCell(generalLedger, glLine, "counter_account").Action, reportingRevenueId, "2026-03-01", "2026-03-31");
    }

    private static async Task<ReportExecutionResponseDto> ExecuteAsync(HttpClient client, string reportCode, ReportExecutionRequestDto request)
    {
        using var resp = await client.PostAsJsonAsync($"/api/reports/{reportCode}/execute", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static ReportCellDto GetCell(ReportExecutionResponseDto report, ReportSheetRowDto row, string columnCode)
    {
        var columnIndex = report.Sheet.Columns
            .Select((column, index) => new { column.Code, index })
            .Single(x => string.Equals(x.Code, columnCode, StringComparison.OrdinalIgnoreCase))
            .index;
        return row.Cells[columnIndex];
    }

    private static void AssertAccountCardAction(ReportCellActionDto? action, Guid expectedAccountId, string expectedFrom, string expectedTo)
    {
        action.Should().NotBeNull();
        action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        action.Report.Should().NotBeNull();
        action.Report!.ReportCode.Should().Be("accounting.account_card");
        action.Report.Parameters.Should().Equal(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = expectedFrom,
            ["to_utc"] = expectedTo
        });
        action.Report.Filters.Should().ContainKey("account_id");
        action.Report.Filters!["account_id"].Value.GetGuid().Should().Be(expectedAccountId);
    }

    private static async Task CreateAndPostGeneralJournalEntryAsync(HttpClient submitterClient, HttpClient approverClient, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var createResp = await submitterClient.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(
                DateUtc: new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc)));
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
                Memo: "Canonical reporting smoke",
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

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
