using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Accounting;
using NGB.Contracts.Admin;
using NGB.Contracts.Reporting;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Core.Reporting;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class FinancialStatementsHttpTests
{
    private static readonly string[] Reports =
    [
        AccountingReportCodes.BalanceSheet,
        AccountingReportCodes.IncomeStatement,
        AccountingReportCodes.CashFlowStatementIndirect,
        AccountingReportCodes.StatementOfChangesInEquity
    ];

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Built_in_statements_reconcile_known_postings_and_preserve_inactive_account_history(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Statement reviewers");
        }
        using var admin = await host.CreateClientAsync(username);
        var cash = await SeedAsync(admin);
        var balance = await ExecuteAsync(admin, AccountingReportCodes.BalanceSheet);
        Assert.Equal(1170m, Amount(balance, "Total Assets"));
        Assert.Equal(50m, Amount(balance, "Total Liabilities"));
        Assert.Equal(1120m, Amount(balance, "Total Equity"));
        Assert.Equal(0m, Amount(balance, "Difference"));

        var income = await ExecuteAsync(admin, AccountingReportCodes.IncomeStatement);
        Assert.Equal(250m, Amount(income, "Total Income"));
        Assert.Equal(130m, Amount(income, "Total Expenses"));
        Assert.Equal(120m, Amount(income, "Net Income"));
        var equity = await ExecuteAsync(admin, AccountingReportCodes.StatementOfChangesInEquity);
        var totalEquity = Assert.Single(equity.Sheet.Rows, row => row.RowKind == ReportRowKind.Total);
        Assert.Equal(new[] { 0m, 1120m, 1120m }, totalEquity.Cells.Skip(1).Select(cell => cell.Value!.Value.GetDecimal()));
        var cashFlow = await ExecuteAsync(admin, AccountingReportCodes.CashFlowStatementIndirect);
        Assert.Equal(170m, Amount(cashFlow, "Net cash from operating activities"));
        Assert.Equal(0m, Amount(cashFlow, "Net cash from investing activities"));
        Assert.Equal(1000m, Amount(cashFlow, "Net cash from financing activities"));
        Assert.Equal(1170m, Amount(cashFlow, "Cash and cash equivalents at end of period"));

        using var inactive = await admin.PostAsync($"/api/chart-of-accounts/{cash.AccountId}/set-active?isActive=false", null);
        await EnsureSuccessAsync(inactive);
        var historicalBalance = await ExecuteAsync(admin, AccountingReportCodes.BalanceSheet);
        Assert.Equal(1170m, Amount(historicalBalance, "Total Assets"));
        var assets = Assert.Single(historicalBalance.Sheet.Rows,
            row => row.RowKind == ReportRowKind.Group && row.Cells[0].Display == "Assets");
        Assert.NotEmpty(assets.ChildrenPath!);
        var assetDetails = await PostAsync<ReportExecutionResponseDto>(admin,
            "/api/reports/" + AccountingReportCodes.BalanceSheet + "/execute",
            Request(AccountingReportCodes.BalanceSheet) with { GroupPath = assets.ChildrenPath });
        Assert.Contains(assetDetails.Sheet.Rows,
            row => row.RowKind == ReportRowKind.Detail && row.Cells[0].Display!.Contains(cash.Code, StringComparison.Ordinal));
        Assert.Equal(1170m, Amount(await ExecuteAsync(admin, AccountingReportCodes.CashFlowStatementIndirect),
            "Cash and cash equivalents at end of period"));
    }

    [Fact]
    public async Task Statement_metadata_dates_and_interactive_bounds_are_enforced()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        foreach (var code in Reports)
        {
            var definition = (await admin.GetFromJsonAsync<ReportDefinitionDto>("/api/report-definitions/" + code, Json))!;
            Assert.Equal(ReportExecutionMode.Canonical, definition.Mode);
            Assert.All(definition.Parameters!, parameter => Assert.True(parameter.IsRequired));
            var request = Request(code);
            var empty = await PostAsync<ReportExecutionResponseDto>(admin, "/api/reports/" + code + "/execute", request);
            Assert.False(empty.HasMore);
            Assert.Null(empty.NextCursor);
            Assert.InRange(empty.Sheet.Rows.Count, 0, definition.Capabilities!.MaxVisibleRows!.Value);
            foreach (var invalid in new[]
            {
                request with { Parameters = null },
                request with { DisablePaging = true },
                request with { Offset = 1 },
                request with { Cursor = "invalid-cursor" }
            })
            {
                using var response = await admin.PostAsJsonAsync("/api/reports/" + code + "/execute", invalid);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                    $"{code}: expected HTTP 400, received {(int)response.StatusCode}.");
            }
        }
    }

    [Fact]
    public async Task Built_in_statements_require_current_report_permissions()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "inspection.statement-observer", "Administrator");
        foreach (var username in new[] { "reader", "inactive-administrator", "imitator" })
        {
            using var client = await host.CreateClientAsync(username);
            foreach (var code in Reports)
            {
                using var definition = await client.GetAsync("/api/report-definitions/" + code);
                using var execution = await client.PostAsJsonAsync("/api/reports/" + code + "/execute", Request(code));
                Assert.Equal(HttpStatusCode.Forbidden, definition.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, execution.StatusCode);
            }
        }
        using var anonymous = host.CreateAnonymousClient();
        foreach (var code in Reports)
        {
            using var response = await anonymous.PostAsJsonAsync("/api/reports/" + code + "/execute", Request(code));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static ReportExecutionRequestDto Request(string code)
        => new(Parameters: code == AccountingReportCodes.BalanceSheet
            ? new Dictionary<string, string> { ["as_of_utc"] = "2026-05-31" }
            : new Dictionary<string, string> { ["from_utc"] = "2026-05-01", ["to_utc"] = "2026-05-31" });

    private static Task<ReportExecutionResponseDto> ExecuteAsync(HttpClient admin, string code)
        => PostAsync<ReportExecutionResponseDto>(admin, "/api/reports/" + code + "/execute", Request(code));

    private static decimal Amount(ReportExecutionResponseDto report, string label)
        => Assert.Single(report.Sheet.Rows, row => row.Cells[0].Display == label).Cells[^1].Value!.Value.GetDecimal();

    private static async Task<ChartOfAccountsAccountDto> SeedAsync(HttpClient admin)
    {
        var metadata = (await admin.GetFromJsonAsync<ChartOfAccountsMetadataDto>("/api/chart-of-accounts/metadata"))!;
        var financing = metadata.CashFlowLineOptions.First(line => line.AllowedRoles.Contains("FinancingCounterparty")).Value;
        var workingCapital = metadata.CashFlowLineOptions.First(line => line.AllowedRoles.Contains("WorkingCapital")).Value;
        var cash = await AccountAsync(admin, "statement.cash", "Statement cash", "Asset", "CashEquivalent");
        var equity = await AccountAsync(admin, "statement.equity", "Statement capital", "Equity", "FinancingCounterparty", financing);
        var income = await AccountAsync(admin, "statement.income", "Statement income", "Income");
        var expense = await AccountAsync(admin, "statement.expense", "Statement expense", "Expense");
        var payable = await AccountAsync(admin, "statement.payable", "Statement payable", "Liability", "WorkingCapital", workingCapital);
        await PostJournalAsync(admin, cash.AccountId, equity.AccountId, 1000m);
        await PostJournalAsync(admin, cash.AccountId, income.AccountId, 250m);
        await PostJournalAsync(admin, expense.AccountId, cash.AccountId, 80m);
        await PostJournalAsync(admin, expense.AccountId, payable.AccountId, 50m);
        return cash;
    }

    private static Task<ChartOfAccountsAccountDto> AccountAsync(
        HttpClient admin, string code, string name, string type, string? role = null, string? line = null)
        => PostAsync<ChartOfAccountsAccountDto>(admin, "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(code, name, type, CashFlowRole: role, CashFlowLineCode: line));

    private static async Task PostJournalAsync(HttpClient admin, Guid debit, Guid credit, decimal amount)
    {
        var draft = await PostAsync<GeneralJournalEntryDetailsDto>(admin, "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc)));
        var path = "/api/accounting/general-journal-entries/" + draft.Document.Id;
        using var header = await admin.PutAsJsonAsync(path + "/header",
            new UpdateGeneralJournalEntryHeaderRequestDto("statement-fixture", ReasonCode: "STATEMENT", Memo: "Known statement totals"));
        await EnsureSuccessAsync(header);
        using var lines = await admin.PutAsJsonAsync(path + "/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto("statement-fixture",
            [
                new((int)GeneralJournalEntryModels.LineSide.Debit, debit, amount, null),
                new((int)GeneralJournalEntryModels.LineSide.Credit, credit, amount, null)
            ]));
        await EnsureSuccessAsync(lines);
        foreach (var action in new[] { "submit", "approve", "post" })
        {
            using var response = await admin.PostAsync(path + "/" + action, null);
            await EnsureSuccessAsync(response);
        }
    }
}
