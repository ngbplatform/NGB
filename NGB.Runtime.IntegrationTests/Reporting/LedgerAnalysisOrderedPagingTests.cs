using System.Text.Json;
using Dapper;
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
public sealed class LedgerAnalysisOrderedPagingTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ordered_pages_preserve_duplicate_labels_amounts_support_ids_and_old_cursors(
        bool descendingLabels, bool implicitSort)
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        var (cash, revenue, expense) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await DirectAccountingExportsTests.ExecuteAsync(host, """
            INSERT INTO documents(id, type_code, number, date_utc, status)
            VALUES
                (md5('ordered-a')::uuid, 'it_doc_a', 'SAME', '2026-09-01', 1),
                (md5('ordered-b')::uuid, 'it_doc_b', 'SAME', '2026-09-01', 1),
                (md5('ordered-c')::uuid, 'it_doc_a', 'OTHER', '2026-09-01', 1);
            INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount, is_storno)
            SELECT md5(v.document)::uuid, v.period,
                   CASE WHEN v.amount < 0 THEN @revenue ELSE @cash END,
                   CASE WHEN v.expense THEN @expense WHEN v.amount < 0 THEN @cash ELSE @revenue END,
                   ABS(v.amount), v.amount < 0
            FROM (VALUES
                ('ordered-a', '2026-08-31 23:59:59+00'::timestamptz, 1000::numeric, false),
                ('ordered-a', '2026-09-01 00:00:00+00'::timestamptz, 10::numeric, false),
                ('ordered-a', '2026-09-01 00:00:00+00'::timestamptz, 2::numeric, false),
                ('ordered-b', '2026-09-01 00:00:00+00'::timestamptz, 3::numeric, false),
                ('ordered-c', '2026-09-01 00:00:00+00'::timestamptz, -1::numeric, false),
                ('ordered-c', '2026-09-01 12:30:00+00'::timestamptz, 4::numeric, true),
                ('missing-document', '2026-09-02 00:00:00+00'::timestamptz, 7::numeric, false),
                ('ordered-c', '2026-09-30 23:59:59.999999+00'::timestamptz, 5::numeric, false),
                ('ordered-c', '2026-10-01 00:00:00+00'::timestamptz, 1000::numeric, false)
            ) v(document, period, amount, expense);
            """, new { cash, revenue, expense });
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var optimized = scope.ServiceProvider.GetRequiredService<PostgresReportDatasetExecutor>();
        var raw = new PostgresReportDatasetExecutor(uow, new(new([new RawSource()])));
        var request = Request();
        if (descendingLabels)
            request = request with { Sorts = [request.Sorts[0], request.Sorts[1] with { Direction = ReportSortDirection.Desc }] };
        if (implicitSort)
            request = request with { Sorts = [] };

        var expected = await raw.ExecuteAsync(request, default);
        var actual = await optimized.ExecuteAsync(request, default);
        actual.Rows.Should().BeEquivalentTo(expected.Rows, options => options.WithStrictOrdering());
        actual.Rows.Sum(row => (decimal)row.Values["debit"]!).Should().Be(32m);
        actual.Rows.Sum(row => (decimal)row.Values["credit"]!).Should().Be(32m);
        actual.Rows.Where(row => (string)row.Values["document"]! == "SAME")
            .Should().OnlyContain(row => row.Values["__support_document_id"] == null);
        actual.Rows.Should().Contain(row => row.Values["__support_document_id"] is Guid);

        foreach (var oldFirst in new[] { true, false })
        {
            var seen = new List<PostgresReportExecutionRow>();
            string? cursor = null;
            var pageNumber = 0;
            do
            {
                var pageRequest = request with { Paging = new(0, pageNumber == 0 ? 1 : 2, cursor) };
                var oldPage = await raw.ExecuteAsync(pageRequest, default);
                var newPage = await optimized.ExecuteAsync(pageRequest, default);
                newPage.Should().BeEquivalentTo(oldPage, options => options.WithStrictOrdering());
                var page = (pageNumber % 2 == 0) == oldFirst ? oldPage : newPage;
                seen.AddRange(page.Rows);
                if (!page.HasMore)
                    break;

                page.NextCursor.Should().NotBeNullOrEmpty().And.NotBe(cursor);
                cursor = page.NextCursor;
                pageNumber++;
                pageNumber.Should().BeLessThan(20);
            } while (true);

            seen.Should().BeEquivalentTo(expected.Rows, options => options.WithStrictOrdering());
        }

        var filtered = request with
        {
            Predicates = [new("account_id", "account_id", "Account", "uuid", new(JsonSerializer.SerializeToElement(cash)))]
        };
        var filteredActual = await optimized.ExecuteAsync(filtered, default);
        var filteredExpected = await raw.ExecuteAsync(filtered, default);
        filteredActual.Rows.Should().BeEquivalentTo(filteredExpected.Rows, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task First_page_uses_an_ordered_index_and_bounds_document_lookups_without_a_full_sort()
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await DirectAccountingExportsTests.ExecuteAsync(host, """
            INSERT INTO documents(id, type_code, number, date_utc, status)
            SELECT md5('ordered-plan-' || g)::uuid, 'it_doc_a', 'PLAN-' || g, '2026-09-01', 1
            FROM generate_series(1, 20000) g;
            INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
            SELECT md5('ordered-plan-' || g)::uuid, '2026-09-01'::timestamptz + (g % 30) * INTERVAL '1 day',
                   @cash, @revenue, 1
            FROM generate_series(1, 20000) g
            ORDER BY g % 30, g;
            ANALYZE accounting_register_main;
            ANALYZE documents;
            ANALYZE accounting_accounts;
            """, new { cash, revenue });
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var statement = scope.ServiceProvider.GetRequiredService<PostgresReportSqlBuilder>().Build(Request());
        await uow.EnsureConnectionOpenAsync();
        var json = await uow.Connection.QuerySingleAsync<string>(
            "EXPLAIN (ANALYZE, BUFFERS, TIMING OFF, FORMAT JSON) " + statement.Sql, statement.Parameters);
        using var plan = JsonDocument.Parse(json);
        var nodes = Nodes(plan.RootElement[0].GetProperty("Plan")).ToArray();

        nodes.Should().Contain(node => node.GetProperty("Node Type").GetString() == "Incremental Sort");
        nodes.Any(node => node.TryGetProperty("Index Name", out var index)
            && index.GetString() == "ix_acc_reg_general_journal_page_order").Should().BeTrue(json);
        nodes.Where(node => node.TryGetProperty("Relation Name", out var name) && name.GetString() == "documents")
            .Sum(node => node.GetProperty("Actual Loops").GetDouble()).Should().BeLessThan(2000);
        plan.RootElement[0].GetProperty("Plan").GetProperty("Temp Written Blocks").GetInt32().Should().Be(0);
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            foreach (var descendant in Nodes(child))
                yield return descendant;
        }
    }

    private static PostgresReportExecutionRequest Request() => new(
        "accounting.ledger.analysis", [], [],
        [new("period_utc", "period", "Period", "datetime"),
         new("account_display", "account", "Account", "string"),
         new("document_display", "document", "Document", "string")],
        [new("debit_amount", "debit", "Debit", "decimal", ReportAggregationKind.Sum),
         new("credit_amount", "credit", "Credit", "decimal", ReportAggregationKind.Sum),
         new("net_amount", "net", "Net", "decimal", ReportAggregationKind.Sum)],
        [new("period_utc", null, ReportSortDirection.Asc), new("account_display", null, ReportSortDirection.Asc)],
        [], new Dictionary<string, object?>
        {
            ["from_utc"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["to_utc_exclusive"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        }, new(0, 100));

    private sealed class RawSource : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        {
            var source = new AccountingLedgerAnalysisPostgresDatasetSource().GetDatasets().Single();
            return [new(source.DatasetCodeNorm, source.FromSql, source.Fields.Values.ToArray(),
                source.Measures.Values.ToArray(), source.BaseWhereSql,
                source.CursorKeyFields.Select(field => field.FieldCodeNorm).ToArray())];
        }
    }
}
