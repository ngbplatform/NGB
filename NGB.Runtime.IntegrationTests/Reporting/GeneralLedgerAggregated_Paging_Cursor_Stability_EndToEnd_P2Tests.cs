using FluentAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class GeneralLedgerAggregated_Paging_Cursor_Stability_EndToEnd_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Bounded_candidates_keep_future_dimension_groups_and_skip_already_aggregated_dates_without_query_growth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var firstDimension = Guid.NewGuid();
        var secondDimension = Guid.NewGuid();
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@firstDimension),(@secondDimension);
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,debit_dimension_set_id,amount)
              SELECT md5('bounded-gl-'||g)::uuid,day.period,@cash,@revenue,day.dimension,day.amount
              FROM generate_series(1,1001) g CROSS JOIN (VALUES
                ('2026-01-01'::timestamptz,@firstDimension::uuid,1),
                ('2026-01-03'::timestamptz,@firstDimension::uuid,2),
                ('2026-01-05'::timestamptz,@secondDimension::uuid,4)) day(period,dimension,amount);
            """, new { cash, revenue, firstDimension, secondDimension });
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPageReader>();
        var full = await reader.GetPageAsync(new() { AccountId = cash, FromInclusive = new(2026, 1, 1), ToInclusive = new(2026, 1, 1), DisablePaging = true });
        var seen = new List<(Guid Document, Guid Dimension, decimal Debit)>();
        GeneralLedgerAggregatedLineCursor? cursor = null;
        var running = 0m;
        do
        {
            using var probe = new NGB.Testing.Reporting.ReportPerformanceProbe("accounting.general_ledger_aggregated", "bounded-multidate");
            var page = await reader.GetPageAsync(new() { AccountId = cash, FromInclusive = new(2026, 1, 1), ToInclusive = new(2026, 1, 1),
                PageSize = 137, Cursor = cursor, IncludePrefixDelta = true });
            page.PrefixDelta.Should().Be(running);
            foreach (var row in page.Lines) { seen.Add((row.DocumentId, row.DimensionSetId, row.DebitAmount)); running += row.Delta; }
            probe.CommandCount.Should().BeLessThanOrEqualTo(15);
            cursor = page.NextCursor;
            if (!page.HasMore) break;
            cursor.Should().NotBeNull();
        } while (true);
        seen.Should().Equal(full.Lines.Select(r => (r.DocumentId, r.DimensionSetId, r.DebitAmount)));
        seen.Should().HaveCount(2002);
        running.Should().Be(7007);
    }

    [Fact]
    public async Task Cursor_never_splits_a_document_group_whose_postings_span_multiple_dates()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
            VALUES (@first,'2026-01-01',@cash,@revenue,10),(@first,'2026-01-03',@cash,@revenue,20),
                   (@second,'2026-01-02',@cash,@revenue,40);
            """, new { first, second, cash, revenue });
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPageReader>();
        var request = new GeneralLedgerAggregatedPageRequest
        {
            AccountId = cash, FromInclusive = new(2026, 1, 1), ToInclusive = new(2026, 1, 1), PageSize = 1
        };
        var one = await reader.GetPageAsync(request);
        one.Lines.Should().ContainSingle().Which.DebitAmount.Should().Be(30);
        var two = await reader.GetPageAsync(new GeneralLedgerAggregatedPageRequest { AccountId = cash, FromInclusive = request.FromInclusive, ToInclusive = request.ToInclusive, PageSize = 1, Cursor = one.NextCursor, IncludePrefixDelta = true });
        two.Lines.Should().ContainSingle().Which.DocumentId.Should().Be(second);
        two.PrefixDelta.Should().Be(30);
        two.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GeneralLedgerAggregatedReportReader_Pages_Through_Large_Dataset_Without_Gaps_And_With_Running_Continuity()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var expectedTotal = 0m;
        for (var i = 0; i < 25; i++)
        {
            var amount = 10m + i;
            expectedTotal += amount;
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", amount);
        }

        var identities = new List<string>();
        var running = 0m;
        GeneralLedgerAggregatedReportCursor? cursor = null;
        var pageIndex = 0;
        GeneralLedgerAggregatedReportPage? lastPage = null;

        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            var page = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 7,
                    Cursor = cursor
                },
                CancellationToken.None);

            if (pageIndex == 0)
                page.OpeningBalance.Should().Be(0m);
            else
                page.OpeningBalance.Should().Be(running);

            page.Lines.Should().NotBeEmpty();
            foreach (var line in page.Lines)
            {
                running += line.DebitAmount - line.CreditAmount;
                line.RunningBalance.Should().Be(running);
                identities.Add(GetIdentity(line));
            }

            lastPage = page;
            pageIndex++;

            if (!page.HasMore || page.NextCursor is null)
                break;

            cursor = page.NextCursor;
        }

        identities.Should().HaveCount(25);
        identities.Should().OnlyHaveUniqueItems();
        running.Should().Be(expectedTotal);
        lastPage.Should().NotBeNull();
        lastPage!.TotalDebit.Should().Be(expectedTotal);
        lastPage.TotalCredit.Should().Be(0m);
        lastPage.ClosingBalance.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task GeneralLedgerAggregatedReportReader_Paging_IsStable_When_New_Rows_Are_Inserted_Before_The_Cursor()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", 10m + i);

        GeneralLedgerAggregatedReportPage page1;
        GeneralLedgerAggregatedReportPage page2Before;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page1 = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2
                },
                CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page2Before = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2,
                    Cursor = page1.NextCursor
                },
                CancellationToken.None);
        }

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 999m);

        GeneralLedgerAggregatedReportPage page2After;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page2After = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2,
                    Cursor = page1.NextCursor
                },
                CancellationToken.None);
        }

        page2After.OpeningBalance.Should().Be(page2Before.OpeningBalance + 999m);
        page2After.Lines.Select(GetIdentity).Should().Equal(page2Before.Lines.Select(GetIdentity));
        page2After.Lines.Select(x => x.RunningBalance).Should().Equal(page2Before.Lines.Select(x => x.RunningBalance + 999m));
        page2After.TotalDebit.Should().Be(page2Before.TotalDebit + 999m);
        page2After.ClosingBalance.Should().Be(page2Before.ClosingBalance + 999m);
    }

    private static string GetIdentity(GeneralLedgerAggregatedReportLine line)
        => $"{line.PeriodUtc:O}|{line.DocumentId:D}|{line.CounterAccountCode}|{line.CounterAccountId:D}|{line.DimensionSetId:D}";
}
