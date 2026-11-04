using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NGB.Contracts.Accounting;
using NGB.Contracts.Admin;
using NGB.Contracts.Reporting;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Core.Reporting;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class LedgerReportsHttpTests
{
    private static readonly string[] Reports =
    [
        AccountingReportCodes.TrialBalance,
        AccountingReportCodes.GeneralJournal,
        AccountingReportCodes.AccountCard,
        AccountingReportCodes.GeneralLedgerAggregated
    ];

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Journal_card_ledger_and_trial_balance_reconcile_the_same_postings_across_cursor_pages(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Ledger supervisors");
        }
        using var admin = await host.CreateClientAsync(username);
        var account = await SeedAsync(admin);
        var journal = await PagesAsync(admin, AccountingReportCodes.GeneralJournal, Request());
        var journalRows = journal.Rows.Where(row => row.RowKind == ReportRowKind.Detail).ToArray();
        Assert.Equal(3, journalRows.Length);
        Assert.Equal(155m, journalRows.Sum(row => Number(journal, row, "amount")));
        Assert.All(journalRows, row => Assert.False(row.Cells[Index(journal, "is_storno")].Value!.Value.GetBoolean()));
        Assert.Equal(3, journalRows.Select(row => row.Cells[Index(journal, "document")].Display).Distinct().Count());

        var filtered = await PagesAsync(admin, AccountingReportCodes.GeneralJournal,
            Request() with { Filters = Filter("debit_account_id", account.AccountId) });
        Assert.Equal(2, filtered.Rows.Count(row => row.RowKind == ReportRowKind.Detail));
        Assert.Equal(125m, filtered.Rows.Where(row => row.RowKind == ReportRowKind.Detail).Sum(row => Number(filtered, row, "amount")));
        var storno = await PagesAsync(admin, AccountingReportCodes.GeneralJournal,
            Request() with { Filters = Filter("is_storno", true) });
        Assert.Empty(storno.Rows);

        foreach (var code in new[] { AccountingReportCodes.AccountCard, AccountingReportCodes.GeneralLedgerAggregated })
        {
            var report = await PagesAsync(admin, code, Request() with { Filters = Filter("account_id", account.AccountId) });
            var details = report.Rows.Where(row => row.RowKind == ReportRowKind.Detail).ToArray();
            Assert.Equal(3, details.Length);
            Assert.Equal(125m, details.Sum(row => Number(report, row, "debit_amount")));
            Assert.Equal(30m, details.Sum(row => Number(report, row, "credit_amount")));
            Assert.Equal(95m, details.Sum(row => Number(report, row, "delta")));
            Assert.Equal(95m, Number(report, details[^1], "running_balance"));
            Assert.Equal(3, details.Select(row => row.Cells[Index(report, "document")].Display).Distinct().Count());
        }

        var trial = await PostAsync<ReportExecutionResponseDto>(admin,
            "/api/reports/" + AccountingReportCodes.TrialBalance + "/execute", Request() with { Limit = 100 });
        var total = Assert.Single(trial.Sheet.Rows, row => row.RowKind == ReportRowKind.Total);
        Assert.Equal(155m, Number(trial.Sheet, total, "debit_amount"));
        Assert.Equal(155m, Number(trial.Sheet, total, "credit_amount"));
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Ledger_analysis_and_posting_diagnostics_reconcile_known_entries_without_repairing_data(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Diagnostic reviewers");
        }
        using var admin = await host.CreateClientAsync(username);
        var account = await SeedAsync(admin);
        var path = "/api/reports/" + AccountingReportCodes.LedgerAnalysis + "/execute";
        var request = Request() with { Limit = 100 };
        var analysis = await PostAsync<ReportExecutionResponseDto>(admin, path, request);
        Assert.False(analysis.HasMore);
        var groups = analysis.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Group).ToArray();
        Assert.Equal(2, groups.Length);
        var cash = Assert.Single(groups, row => row.Cells[0].Display!.Contains(account.Code, StringComparison.Ordinal));
        Assert.All(cash.Cells.Skip(1), cell => Assert.Null(cell.Value));
        var subtotal = Assert.Single(analysis.Sheet.Rows,
            row => row.RowKind == ReportRowKind.Subtotal && row.Cells[0].Display!.Contains(account.Code, StringComparison.Ordinal));
        Assert.Equal(new[] { 125m, 30m, 95m }, subtotal.Cells.Skip(1).Select(cell => cell.Value!.Value.GetDecimal()));
        var grand = Assert.Single(analysis.Sheet.Rows, row => row.RowKind == ReportRowKind.Total);
        Assert.Equal(new[] { 155m, 155m, 0m }, grand.Cells.Skip(1).Select(cell => cell.Value!.Value.GetDecimal()));

        Assert.NotEmpty(cash.ChildrenPath!);
        var periods = await PostAsync<ReportExecutionResponseDto>(admin, path, request with { GroupPath = cash.ChildrenPath });
        Assert.False(periods.HasMore);
        var days = periods.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Group).ToArray();
        Assert.Equal(3, days.Length);
        Assert.Equal(125m, days.Sum(row => Number(periods.Sheet, row, "debit_amount__sum")));
        Assert.Equal(30m, days.Sum(row => Number(periods.Sheet, row, "credit_amount__sum")));
        Assert.Equal(95m, days.Sum(row => Number(periods.Sheet, row, "net_amount__sum")));
        var filtered = await PostAsync<ReportExecutionResponseDto>(admin, path,
            request with { Filters = Filter("account_id", new[] { account.AccountId }) });
        Assert.Single(filtered.Sheet.Rows, row => row.RowKind == ReportRowKind.Group);
        Assert.Equal(95m, Assert.Single(filtered.Sheet.Rows, row => row.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal());

        var posting = await PagesAsync(admin, AccountingReportCodes.PostingLog,
            new ReportExecutionRequestDto(Limit: 1, Filters: Filter("status", "Completed")));
        Assert.Equal(3, posting.Rows.Count);
        Assert.All(posting.Rows, row =>
        {
            Assert.Equal("Post", row.Cells[Index(posting, "operation")].Display);
            Assert.Equal("Completed", row.Cells[Index(posting, "status")].Display);
        });
        var none = await PagesAsync(admin, AccountingReportCodes.PostingLog,
            new ReportExecutionRequestDto(Limit: 1, Filters: Filter("operation", "Unpost")));
        Assert.Empty(none.Rows);

        var consistency = await PostAsync<ReportExecutionResponseDto>(admin,
            "/api/reports/" + AccountingReportCodes.Consistency + "/execute",
            new ReportExecutionRequestDto(Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-05-01" }));
        Assert.False(consistency.HasMore);
        Assert.Equal(5, consistency.Sheet.Rows.Count);
        Assert.All(consistency.Sheet.Rows, row =>
        {
            Assert.Equal(ReportRowKind.Total, row.RowKind);
            Assert.Equal(0, row.Cells[^1].Value!.Value.GetInt64());
        });
    }

    [Fact]
    public async Task Flat_ledger_analysis_pages_both_posting_sides_and_binds_continuations_to_the_selection()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var account = await SeedAsync(admin);
        var request = Request() with
        {
            Layout = new ReportLayoutDto(
                RowGroups: [],
                ColumnGroups: [],
                Measures:
                [
                    new("debit_amount"),
                    new("credit_amount"),
                    new("net_amount")
                ],
                DetailFields: ["period_utc", "account_display", "document_display"],
                Sorts: [new("period_utc")],
                ShowDetails: true,
                ShowSubtotals: false,
                ShowGrandTotals: false)
        };
        var all = await PagesAsync(admin, AccountingReportCodes.LedgerAnalysis, request);
        Assert.Equal(6, all.Rows.Count);
        Assert.All(all.Rows, row => Assert.Equal(ReportRowKind.Detail, row.RowKind));
        Assert.Equal(155m, all.Rows.Sum(row => Number(all, row, "debit_amount__sum")));
        Assert.Equal(155m, all.Rows.Sum(row => Number(all, row, "credit_amount__sum")));
        Assert.Equal(0m, all.Rows.Sum(row => Number(all, row, "net_amount__sum")));
        Assert.Equal(6, all.Rows.Select(row => (
            row.Cells[Index(all, "account_display")].Display,
            row.Cells[Index(all, "document_display")].Display)).Distinct().Count());

        var filtered = await PagesAsync(admin, AccountingReportCodes.LedgerAnalysis,
            request with { Filters = Filter("account_id", new[] { account.AccountId }) });
        Assert.Equal(3, filtered.Rows.Count);
        Assert.Equal(95m, filtered.Rows.Sum(row => Number(filtered, row, "net_amount__sum")));

        var path = "/api/reports/" + AccountingReportCodes.LedgerAnalysis + "/execute";
        var first = await PostAsync<ReportExecutionResponseDto>(admin, path, request);
        Assert.True(first.HasMore);
        using var changed = await admin.PostAsJsonAsync(path, request with
        {
            Cursor = first.NextCursor,
            Filters = Filter("account_id", new[] { account.AccountId })
        });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
    }

    [Fact]
    public async Task Ledger_report_requirements_and_continuations_are_validated()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var account = await SeedAsync(admin);
        foreach (var code in Reports)
        {
            var definition = (await admin.GetFromJsonAsync<ReportDefinitionDto>("/api/report-definitions/" + code, Json))!;
            Assert.Equal(ReportExecutionMode.Canonical, definition.Mode);
            var request = Request() with
            {
                Filters = code is AccountingReportCodes.AccountCard or AccountingReportCodes.GeneralLedgerAggregated
                    ? Filter("account_id", account.AccountId) : null
            };
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
                    $"{code}: expected 400, received {(int)response.StatusCode}.");
            }
            if (request.Filters is not null)
            {
                using var missing = await admin.PostAsJsonAsync("/api/reports/" + code + "/execute", request with { Filters = null });
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            }
        }

        var first = await PostAsync<ReportExecutionResponseDto>(admin,
            "/api/reports/" + AccountingReportCodes.GeneralJournal + "/execute", Request());
        Assert.True(first.HasMore);
        using var changed = await admin.PostAsJsonAsync("/api/reports/" + AccountingReportCodes.GeneralJournal + "/execute",
            Request() with { Cursor = first.NextCursor, Filters = Filter("debit_account_id", account.AccountId) });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
    }

    [Fact]
    public async Task Ledger_reports_keep_definition_and_execution_access_consistent()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "ledger.observer", "Administrator");
        foreach (var username in new[] { "reader", "inactive-administrator", "imitator" })
        {
            using var client = await host.CreateClientAsync(username);
            foreach (var code in Reports.Concat(new[]
            {
                AccountingReportCodes.LedgerAnalysis, AccountingReportCodes.PostingLog, AccountingReportCodes.Consistency
            }))
            {
                using var definition = await client.GetAsync("/api/report-definitions/" + code);
                using var execution = await client.PostAsJsonAsync("/api/reports/" + code + "/execute", Request());
                Assert.Equal(HttpStatusCode.Forbidden, definition.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, execution.StatusCode);
            }
        }
        using var anonymous = host.CreateAnonymousClient();
        foreach (var code in Reports.Concat(new[]
        {
            AccountingReportCodes.LedgerAnalysis, AccountingReportCodes.PostingLog, AccountingReportCodes.Consistency
        }))
        {
            using var response = await anonymous.PostAsJsonAsync("/api/reports/" + code + "/execute", Request());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static async Task<ReportSheetDto> PagesAsync(HttpClient client, string code, ReportExecutionRequestDto request)
    {
        var rows = new List<ReportSheetRowDto>();
        var cursors = new HashSet<string>();
        ReportExecutionResponseDto page;
        do
        {
            page = await PostAsync<ReportExecutionResponseDto>(client, "/api/reports/" + code + "/execute", request);
            Assert.InRange(page.Sheet.Rows.Count(row => row.RowKind == ReportRowKind.Detail), 0, request.Limit);
            rows.AddRange(page.Sheet.Rows);
            if (page.HasMore)
            {
                Assert.True(cursors.Add(page.NextCursor!));
                Assert.InRange(cursors.Count, 1, 10);
            }
            request = request with { Cursor = page.NextCursor };
        } while (page.HasMore);
        Assert.Null(page.NextCursor);
        return page.Sheet with { Rows = rows };
    }

    private static ReportExecutionRequestDto Request()
        => new(Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-05-01", ["to_utc"] = "2026-05-31" }, Limit: 1);

    private static Dictionary<string, ReportFilterValueDto> Filter(string key, object value)
        => new() { [key] = new(JsonSerializer.SerializeToElement(value)) };

    private static int Index(ReportSheetDto sheet, string code)
        => sheet.Columns.Select(column => column.Code).ToList().IndexOf(code);

    private static decimal Number(ReportSheetDto sheet, ReportSheetRowDto row, string code)
        => row.Cells[Index(sheet, code)].Value!.Value.GetDecimal();

    private static async Task<ChartOfAccountsAccountDto> SeedAsync(HttpClient client)
    {
        var cash = await PostAsync<ChartOfAccountsAccountDto>(client, "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto("ledger.cash", "Ledger cash", "Asset"));
        var payable = await PostAsync<ChartOfAccountsAccountDto>(client, "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto("ledger.payable", "Ledger payable", "Liability"));
        foreach (var (debit, credit, amount, day) in new[]
        {
            (cash.AccountId, payable.AccountId, 100m, 17),
            (payable.AccountId, cash.AccountId, 30m, 18),
            (cash.AccountId, payable.AccountId, 25m, 19)
        })
        {
            var draft = await PostAsync<GeneralJournalEntryDetailsDto>(client, "/api/accounting/general-journal-entries",
                new CreateGeneralJournalEntryDraftRequestDto(new DateTime(2026, 5, day, 12, 0, 0, DateTimeKind.Utc)));
            var path = "/api/accounting/general-journal-entries/" + draft.Document.Id;
            using var header = await client.PutAsJsonAsync(path + "/header",
                new UpdateGeneralJournalEntryHeaderRequestDto("ledger-fixture", ReasonCode: "LEDGER", Memo: "Known ledger totals"));
            await EnsureSuccessAsync(header);
            using var lines = await client.PutAsJsonAsync(path + "/lines",
                new ReplaceGeneralJournalEntryLinesRequestDto("ledger-fixture",
                [
                    new((int)GeneralJournalEntryModels.LineSide.Debit, debit, amount, null),
                    new((int)GeneralJournalEntryModels.LineSide.Credit, credit, amount, null)
                ]));
            await EnsureSuccessAsync(lines);
            foreach (var action in new[] { "submit", "approve", "post" })
            {
                using var response = await client.PostAsync(path + "/" + action, null);
                await EnsureSuccessAsync(response);
            }
        }
        return cash;
    }
}
