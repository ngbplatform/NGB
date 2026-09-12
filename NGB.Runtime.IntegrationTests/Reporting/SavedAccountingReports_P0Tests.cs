using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Reporting.Runs;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class SavedAccountingReports_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static ReportExecutionRequestDto Request => new(Parameters: new Dictionary<string, string>
    { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-30" });

    [Theory]
    [InlineData("accounting.balance_sheet", 1)]
    [InlineData("accounting.income_statement", 4)]
    [InlineData("accounting.statement_of_changes_in_equity", 3)]
    public async Task Financial_statements_stream_more_than_ten_thousand_accounts_and_export_every_row(string code, int section)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ExecuteAsync(host, """
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy)
              SELECT md5('financial-'||g)::uuid,'IT'||lpad(g::text,5,'0'),'Account '||g,@type,@section,0 FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
              SELECT '2026-08-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,3,0 FROM accounting_accounts
              UNION ALL SELECT '2026-09-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,2,0 FROM accounting_accounts;
            """, new { section, type = section == 1 ? 0 : section == 3 ? 2 : 3 });
        var request = code.EndsWith("balance_sheet") ? new ReportExecutionRequestDto(Parameters: new Dictionary<string, string> { ["as_of_utc"] = "2026-09-30" }) : Request;
        var result = await RunAsync(host, code, request);
        result.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(10001);
        var total = result.Rows.Last(r => r.RowKind == ReportRowKind.Total);
        if (section == 3)
            total.Cells.Skip(1).Select(c => c.Value!.Value.GetDecimal()).Should().Equal(-30003m, -20002m, -50005m);
        if (section == 4) total.Cells[1].Value!.Value.GetDecimal().Should().Be(-20002m);
        if (section == 1) result.Rows.Single(r => r.Cells[0].Display == "Total Assets").Cells[1].Value!.Value.GetDecimal().Should().Be(50005m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Statements_match_existing_financial_semantics_across_closed_periods_contra_and_inactive_accounts(int closedMode)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ExecuteAsync(host, """
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy,is_active,is_contra)
              SELECT md5('semantics-'||g)::uuid,'IT'||g,'Account '||g,
                CASE g WHEN 1 THEN 0 WHEN 2 THEN 1 WHEN 3 THEN 2 WHEN 4 THEN 3 WHEN 7 THEN 3 ELSE 4 END,
                g,0,g<>2,g IN (1,4) FROM generate_series(1,8) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
              SELECT '2026-08-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,10,3 FROM accounting_accounts
              UNION ALL SELECT '2026-09-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,2,6 FROM accounting_accounts;
            """, null);
        if (closedMode > 0)
            await ExecuteAsync(host, """
                INSERT INTO accounting_balances(period,account_id,dimension_set_id,opening_balance,closing_balance)
                  SELECT @period,account_id,'00000000-0000-0000-0000-000000000000'::uuid,@opening,@closing FROM accounting_accounts;
                INSERT INTO accounting_closed_periods(period,closed_at_utc,closed_by) VALUES (@period,now(),'test');
                """, new { period = new DateOnly(2026, closedMode == 1 ? 8 : 9, 1), opening = closedMode == 1 ? 0m : 7m, closing = closedMode == 1 ? 7m : 99m });
        foreach (var code in new[] { "accounting.balance_sheet", "accounting.income_statement", "accounting.statement_of_changes_in_equity" })
        {
            var request = code.EndsWith("balance_sheet") ? new ReportExecutionRequestDto(Parameters: new Dictionary<string, string> { ["as_of_utc"] = "2026-09-30" }) : Request;
            await using var scope = host.Services.CreateAsyncScope();
            var definition = await scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>().GetDefinitionAsync(code, default);
            var old = await scope.ServiceProvider.GetServices<IReportSpecializedPlanExecutor>().Single(e => e.ReportCode == code).ExecuteAsync(definition, request, default);
            var saved = await RunAsync(host, code, request);
            saved.Rows.Select(r => (r.RowKind, Cells: string.Join("|", r.Cells.Select(c => c.Display))))
                .Should().Equal(old.PrebuiltSheet!.Rows.Select(r => (r.RowKind, Cells: string.Join("|", r.Cells.Select(c => c.Display)))));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Integrity_checks_cover_all_dimension_keys_and_export_merged_totals(bool closed)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ExecuteAsync(host, """
            INSERT INTO platform_dimension_sets(dimension_set_id) SELECT md5('integrity-'||g)::uuid FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
              SELECT '2026-09-01'::date,@cash,dimension_set_id,2,0 FROM platform_dimension_sets WHERE dimension_set_id<>'00000000-0000-0000-0000-000000000000'::uuid;
            """, new { cash });
        if (closed)
            await ExecuteAsync(host, """
                INSERT INTO accounting_balances(period,account_id,dimension_set_id,opening_balance,closing_balance)
                  SELECT '2026-09-01'::date,account_id,dimension_set_id,0,99 FROM accounting_turnovers;
                """, null);
        var result = await RunAsync(host, "accounting.consistency", new(Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-09-01" }));
        result.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(closed ? 10002 : 1);
        result.Rows.Last().Cells.Last().Value!.Value.GetInt64().Should().Be(closed ? 10002 : 1);
        result.Rows.Single(r => r.Cells[0].Display == "Balance vs turnover").Cells.Last().Value!.Value.GetInt64().Should().Be(closed ? 10001 : 0);
    }

    [Theory]
    [InlineData("accounting.general_journal")]
    [InlineData("accounting.account_card")]
    [InlineData("accounting.general_ledger_aggregated")]
    [InlineData("accounting.posting_log")]
    public async Task Canonical_journals_continue_beyond_ten_thousand_rows_without_losing_running_balances(string code)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ExecuteAsync(host, """
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
              SELECT md5('journal-'||g)::uuid,'2026-09-01'::timestamptz,@cash,@revenue,2 FROM generate_series(1,10001) g;
            INSERT INTO accounting_posting_state(attempt_id,document_id,operation,started_at_utc,completed_at_utc)
              SELECT md5('attempt-'||g)::uuid,md5('journal-'||g)::uuid,1,'2026-09-01'::timestamptz,'2026-09-01'::timestamptz
              FROM generate_series(1,10001) g;
            """, new { cash, revenue });
        var request = Request;
        if (code is "accounting.account_card" or "accounting.general_ledger_aggregated")
            request = request with { Filters = new Dictionary<string, ReportFilterValueDto> { ["account_id"] = new(JsonSerializer.SerializeToElement(cash)) } };
        var sheet = await RunAsync(host, code, request);
        var details = sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail).ToArray();
        details.Should().HaveCount(10001);
        if (code is "accounting.account_card" or "accounting.general_ledger_aggregated")
            details.Last().Cells.Last().Value!.Value.GetDecimal().Should().Be(20002m);
        if (code == "accounting.general_ledger_aggregated") sheet.Rows.Count(r => r.RowKind == ReportRowKind.Total).Should().Be(1);
    }

    internal static async Task<ReportSheetDto> RunAsync(IHost host, string code, ReportExecutionRequestDto request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IReportRunService>();
        var run = await runs.StartAsync(code, "reader", request, default);
        await host.Services.GetRequiredService<ReportRunProcessor>().ProcessNextAsync(default);
        (await runs.GetAsync(code, "reader", run.Id, default))!.Status.Should().Be("Ready",
            string.Join("\n", (host.Services.GetService<ILogger<ReportRunProcessor>>() as SavedComposableReports_P0Tests.CapturedLog)?.Errors ?? []));
        var rows = new List<ReportSheetRowDto>();
        ReportExecutionResponseDto page;
        do
        {
            page = await runs.ReadAsync(code, "reader", run.Id, rows.Count, 317, default);
            rows.AddRange(page.Sheet.Rows);
        } while (page.HasMore);
        page.Total.Should().Be(rows.Count);
        await using var file = await runs.ExportAsync(code, "reader", run.Id, default);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(xml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        sheet.Descendants(ns + "row").Count().Should().Be(rows.Count + (page.Sheet.HeaderRows?.Count ?? 1));
        if (code == "accounting.consistency") sheet.Descendants(ns + "mergeCell").Should().HaveCount(5);
        return page.Sheet with { Rows = rows };
    }

    internal static async Task ExecuteAsync(IHost host, string sql, object? args)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await uow.Connection.ExecuteAsync(sql, args, uow.Transaction);
        await uow.CommitAsync();
    }
}
