using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class DirectAccountingPagesTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("accounting.trial_balance", 0, 1)]
    [InlineData("accounting.balance_sheet", 0, 1)]
    [InlineData("accounting.income_statement", 3, 4)]
    [InlineData("accounting.statement_of_changes_in_equity", 2, 3)]
    public async Task Pages_more_than_ten_thousand_accounts_without_duplicate_rows_or_incomplete_totals(string code, int type, int section)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await DirectAccountingExportsTests.ExecuteAsync(host, """
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy)
            SELECT md5('direct-account-'||g)::uuid,'DP'||lpad(g::text,5,'0'),'Account '||g,@type,@section,0 FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
            SELECT '2026-08-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,3,0 FROM accounting_accounts
            UNION ALL SELECT '2026-09-01'::date,account_id,'00000000-0000-0000-0000-000000000000'::uuid,2,0 FROM accounting_accounts;
            """, new { type, section });
        var parameters = code == "accounting.balance_sheet" ? new Dictionary<string, string> { ["as_of_utc"] = "2026-09-30" }
            : new Dictionary<string, string> { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-30" };
        var request = new ReportExecutionRequestDto(Parameters: parameters, Limit: 233);
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var first = await engine.ExecuteAsync(code, request, default);
        first.Sheet.Rows.Count.Should().BeLessThan(240);
        var paths = first.Sheet.Rows.Where(r => r.ChildrenPath is not null).Select(r => r.ChildrenPath!).ToArray();
        var details = new HashSet<string>();
        var actualTotals = new List<ReportSheetRowDto>();
        if (paths.Length == 0) await ReadAllAsync(null, first);
        else
        {
            first.HasMore.Should().BeFalse();
            actualTotals.AddRange(first.Sheet.Rows.Where(r => r.RowKind == ReportRowKind.Total));
            foreach (var path in paths) await ReadAllAsync(path, null);
        }
        details.Should().HaveCount(10001);
        var export = await DirectAccountingExportsTests.RunAsync(host, code, request);
        actualTotals.Select(Signature).Should().Equal(export.Rows.Where(r => r.RowKind == ReportRowKind.Total).Select(Signature));

        async Task ReadAllAsync(IReadOnlyList<JsonElement>? path, ReportExecutionResponseDto? firstPage)
        {
            string? cursor = null;
            ReportExecutionResponseDto page;
            do
            {
                page = firstPage ?? await engine.ExecuteAsync(code, request with { Cursor = cursor, GroupPath = path }, default);
                firstPage = null;
                page.Sheet.Rows.Count.Should().BeLessThan(240);
                foreach (var row in page.Sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail)) details.Add(row.Cells[0].Display!).Should().BeTrue();
                if (path is null) actualTotals.AddRange(page.Sheet.Rows.Where(r => r.RowKind == ReportRowKind.Total));
                cursor = page.NextCursor;
                if (page.HasMore) cursor.Should().NotBeNullOrEmpty();
            } while (page.HasMore);
        }
        static string Signature(ReportSheetRowDto r) => string.Join('|', r.Cells.Select(c => c.Display));
    }
}
