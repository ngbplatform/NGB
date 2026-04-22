using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountCardPagingContracts_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AccountCardLinePageReader_KeysetPaging_MatchesNonPagedReader_LinesAndOrder()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await SeedMovementsAsync(host);

        IReadOnlyList<AccountCardLine> expected;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardReader>();
            expected = await reader.GetAsync(
                cashId,
                ReportingTestHelpers.Period,
                ReportingTestHelpers.Period,
                ct: CancellationToken.None);
        }

        var actual = new List<AccountCardLine>(expected.Count);
        AccountCardLineCursor? cursor = null;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardPageReader>();

            for (var guard = 0; guard < 10_000; guard++)
            {
                var page = await reader.GetPageAsync(
                    new AccountCardLinePageRequest
                    {
                        AccountId = cashId,
                        FromInclusive = ReportingTestHelpers.Period,
                        ToInclusive = ReportingTestHelpers.Period,
                        PageSize = 4,
                        Cursor = cursor
                    },
                    CancellationToken.None);

                AccountCardPagingContracts.AssertLinePageContract(page, cursor);

                actual.AddRange(page.Lines);

                if (!page.HasMore)
                    break;

                cursor = page.NextCursor;
                cursor.Should().NotBeNull();
            }
        }

        actual.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());

        // Extra sanity: no duplicates.
        actual.Select(x => x.EntryId).Distinct().Count().Should().Be(actual.Count);
    }

    [Fact]
    public async Task AccountCardPagedReportReader_KeysetPaging_MatchesNonPagedReport_LinesTotalsAndCursorCurrency()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await SeedMovementsAsync(host);

        AccountCardReport expected;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            expected = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
                scope.ServiceProvider,
                cashId,
                ReportingTestHelpers.Period,
                ReportingTestHelpers.Period,
                ct: CancellationToken.None);
        }

        var actualLines = new List<AccountCardReportLine>(expected.Lines.Count);
        AccountCardReportCursor? cursor = null;
        var expectedOpening = expected.OpeningBalance;

        decimal? totalDebit = null;
        decimal? totalCredit = null;
        decimal? closingBalance = null;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            for (var guard = 0; guard < 10_000; guard++)
            {
                var page = await reader.GetPageAsync(
                    new AccountCardReportPageRequest
                    {
                        AccountId = cashId,
                        FromInclusive = ReportingTestHelpers.Period,
                        ToInclusive = ReportingTestHelpers.Period,
                        PageSize = 5,
                        Cursor = cursor
                    },
                    CancellationToken.None);

                // Basic shape invariants.
                page.AccountId.Should().Be(expected.AccountId);
                page.AccountCode.Should().Be(expected.AccountCode);
                page.FromInclusive.Should().Be(ReportingTestHelpers.Period);
                page.ToInclusive.Should().Be(ReportingTestHelpers.Period);

                AccountCardPagingContracts.AssertReportPageContract(page, cursor);

                // Totals must be independent of paging and equal to non-paged report.
                totalDebit ??= page.TotalDebit;
                totalCredit ??= page.TotalCredit;
                closingBalance ??= page.ClosingBalance;

                page.TotalDebit.Should().Be(totalDebit.Value);
                page.TotalCredit.Should().Be(totalCredit.Value);
                page.ClosingBalance.Should().Be(closingBalance.Value);

                page.TotalDebit.Should().Be(expected.TotalDebit);
                page.TotalCredit.Should().Be(expected.TotalCredit);
                page.ClosingBalance.Should().Be(expected.ClosingBalance);

                // Opening currency: first page uses report opening, next pages use cursor running balance.
                page.OpeningBalance.Should().Be(expectedOpening);

                if (page.Lines.Count > 0)
                    expectedOpening = page.Lines[^1].RunningBalance;

                actualLines.AddRange(page.Lines);

                if (!page.HasMore)
                {
                    page.NextCursor.Should().BeNull();
                    break;
                }

                cursor = page.NextCursor;
                cursor.Should().NotBeNull();
            }
        }

        actualLines.Should().BeEquivalentTo(expected.Lines, o => o.WithStrictOrdering());

        // Extra sanity: no duplicate keys and strict monotonic ordering by (PeriodUtc, EntryId).
        actualLines.Select(x => (x.PeriodUtc, x.EntryId)).Distinct().Count().Should().Be(actualLines.Count);

        for (var i = 1; i < actualLines.Count; i++)
        {
            var prev = actualLines[i - 1];
            var cur = actualLines[i];

            var ok = prev.PeriodUtc < cur.PeriodUtc ||
                     (prev.PeriodUtc == cur.PeriodUtc && prev.EntryId < cur.EntryId);

            ok.Should().BeTrue("AccountCard report lines must be ordered by (PeriodUtc, EntryId) ASC");
        }
    }

    private static async Task SeedMovementsAsync(IHost host)
    {
        // We keep timestamps intentionally repeating to force tie-break by EntryId.
        var baseUtc = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 23; i++)
        {
            var dt = baseUtc.AddMinutes(i % 4); // repeats
            var doc = Guid.CreateVersion7();

            if (i % 2 == 1)
            {
                // +Cash (Debit Cash)
                await ReportingTestHelpers.PostAsync(host, doc, dt, debitCode: "50", creditCode: "90.1", amount: i);
            }
            else
            {
                // -Cash (Credit Cash)
                await ReportingTestHelpers.PostAsync(host, doc, dt, debitCode: "91", creditCode: "50", amount: i);
            }
        }
    }
}
