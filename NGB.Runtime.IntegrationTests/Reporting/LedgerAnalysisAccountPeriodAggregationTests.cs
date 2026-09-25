using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class LedgerAnalysisAccountPeriodAggregationTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(ReportTimeGrain.Day, false)]
    [InlineData(ReportTimeGrain.Week, false)]
    [InlineData(ReportTimeGrain.Month, false)]
    [InlineData(ReportTimeGrain.Quarter, false)]
    [InlineData(ReportTimeGrain.Year, false)]
    [InlineData(ReportTimeGrain.Month, true)]
    public async Task Account_period_sums_preserve_raw_results_filters_selection_and_paging(ReportTimeGrain? grain, bool pivot)
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await DirectAccountingExportsTests.ExecuteAsync(host, """
            INSERT INTO documents(id,type_code,number,date_utc,status)
            VALUES (md5('account-period')::uuid,'it_doc_a','ACCOUNT-PERIOD','2026-09-01'::timestamptz,1);
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
            SELECT md5('account-period')::uuid, v.period, @cash, @revenue, v.amount
            FROM (VALUES
                ('2026-08-31 23:59:59+00'::timestamptz, 1000::numeric),
                ('2026-09-07 12:00:00+00'::timestamptz, 3::numeric),
                ('2026-09-07 12:00:00+00'::timestamptz, 5::numeric),
                ('2026-09-07 18:00:00+00'::timestamptz, 1::numeric),
                ('2026-09-14 00:00:00+00'::timestamptz, 10::numeric),
                ('2026-10-01 00:00:00+00'::timestamptz, 20::numeric),
                ('2026-11-01 00:00:00+00'::timestamptz, 1000::numeric)) v(period,amount);
            """, new { cash, revenue });
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var optimized = scope.ServiceProvider.GetRequiredService<PostgresReportDatasetExecutor>();
        var raw = new PostgresReportDatasetExecutor(uow, new(new([new RawSource()])));
        var period = new PostgresReportGroupingSelection("period_utc", "period", "Period", "datetime", grain);
        var account = new PostgresReportGroupingSelection("account_display", "account", "Account", "string");
        var request = new PostgresReportExecutionRequest("accounting.ledger.analysis",
            pivot ? [account] : [account, period], pivot ? [period] : [], [],
            [new("debit_amount", "debit", "Debit", "decimal", ReportAggregationKind.Sum),
             new("credit_amount", "credit", "Credit", "decimal", ReportAggregationKind.Sum),
             new("net_amount", "net", "Net", "decimal", ReportAggregationKind.Sum)],
            [], [], new Dictionary<string, object?>
            {
                ["from_utc"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ["to_utc_exclusive"] = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            }, new(0, 100));

        var actual = await optimized.ExecuteAsync(request, default);
        var expected = await raw.ExecuteAsync(request, default);
        actual.Rows.Should().BeEquivalentTo(expected.Rows, o => o.WithStrictOrdering());
        actual.Rows.Sum(r => (decimal)r.Values["debit"]!).Should().Be(39m);
        actual.Rows.Sum(r => (decimal)r.Values["credit"]!).Should().Be(39m);
        actual.Rows.Sum(r => (decimal)r.Values["net"]!).Should().Be(0m);
        actual.Rows.Should().OnlyContain(r => r.Values["__support_account_id"] is Guid);

        var instant = JsonSerializer.SerializeToElement(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc));
        var filtered = request with { Predicates = [new("period_utc", "period", "Period", "datetime", new(instant))] };
        var selected = request with { Selection = new([new("period_utc")], [[instant]]) };
        foreach (var subset in new[] { filtered, selected })
        {
            var subsetActual = await optimized.ExecuteAsync(subset, default);
            var subsetExpected = await raw.ExecuteAsync(subset, default);
            subsetActual.Rows.Should().BeEquivalentTo(subsetExpected.Rows, o => o.WithStrictOrdering());
            subsetActual.Rows.Sum(r => (decimal)r.Values["debit"]!).Should().Be(8m);
            subsetActual.Rows.Sum(r => (decimal)r.Values["credit"]!).Should().Be(8m);
        }

        var paged = new List<PostgresReportExecutionRow>();
        string? cursor = null;
        do
        {
            var page = await optimized.ExecuteAsync(request with { Paging = new(0, 1, cursor) }, default);
            paged.AddRange(page.Rows);
            if (!page.HasMore) break;
            page.NextCursor.Should().NotBeNullOrEmpty().And.NotBe(cursor);
            cursor = page.NextCursor;
            paged.Count.Should().BeLessThan(20);
        } while (true);
        paged.Should().BeEquivalentTo(expected.Rows, o => o.WithStrictOrdering());
    }

    private sealed class RawSource : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        {
            var source = new AccountingLedgerAnalysisPostgresDatasetSource().GetDatasets().Single();
            return [new(source.DatasetCodeNorm, source.FromSql, source.Fields.Values.ToArray(),
                source.Measures.Values.ToArray(), source.BaseWhereSql)];
        }
    }
}
