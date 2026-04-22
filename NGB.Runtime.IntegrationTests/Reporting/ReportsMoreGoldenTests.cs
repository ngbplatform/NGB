using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.Periods;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ReportsMoreGoldenTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PrevPeriodUtc = new(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PrevPeriod = DateOnly.FromDateTime(PrevPeriodUtc);

    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task TrialBalance_OpeningFromLatestClosedSnapshot_Golden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Seed CoA (minimal for this test).
        await SeedReportingCoAAsync(host);

        var docPrev1 = Guid.CreateVersion7();
        var docPrev2 = Guid.CreateVersion7();

        // Prev period movements:
        // Cash +1000 (Equity -1000), then Cash -200 (Expenses +200)
        await PostAsync(host, docPrev1, PrevPeriodUtc, "50", "80", 1000m);
        await PostAsync(host, docPrev2, PrevPeriodUtc, "91", "50", 200m);

        // Closing PrevPeriod produces balances snapshot used as opening for the next month.
        await CloseMonthAsync(host, PrevPeriod);

        // Current period movements:
        var docCur1 = Guid.CreateVersion7();
        var docCur2 = Guid.CreateVersion7();
        await PostAsync(host, docCur1, PeriodUtc, "50", "90.1", 500m);
        await PostAsync(host, docCur2, PeriodUtc, "60", "50", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var trial = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await trial.GetAsync(Period, Period, CancellationToken.None);

        // Cash opening should come from PrevPeriod closed snapshot: 1000 - 200 = 800.
        var cash = rows.Single(r => r.AccountCode == "50");
        cash.OpeningBalance.Should().Be(800m);
        cash.DebitAmount.Should().Be(500m);
        cash.CreditAmount.Should().Be(50m);
        cash.ClosingBalance.Should().Be(1250m);

        // Trial Balance must balance: sum(ClosingBalance * sign) isn't available here,
        // but we can check that total debits == total credits for the range activity.
        rows.Sum(r => r.DebitAmount).Should().Be(rows.Sum(r => r.CreditAmount));
    }

    [Fact]
    public async Task GeneralJournal_Paging_TieBreakByEntryId_NoGapsNoDuplicates()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedReportingCoAAsync(host);

        // Create multiple entries on the exact same PeriodUtc to exercise the (PeriodUtc, EntryId) cursor.
        var documents = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();

        // Alternate posting patterns, but keep all in the same UTC day/time.
        await PostAsync(host, documents[0], PeriodUtc, "50", "80", 1000m); // equity injection
        await PostAsync(host, documents[1], PeriodUtc, "50", "90.1", 100m); // revenue
        await PostAsync(host, documents[2], PeriodUtc, "91", "50", 10m); // expense paid cash
        await PostAsync(host, documents[3], PeriodUtc, "50", "90.1", 200m); // revenue
        await PostAsync(host, documents[4], PeriodUtc, "91", "50", 20m); // expense paid cash

        await using var scope = host.Services.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        // Read the full ordered list as oracle.
        var full = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100
            },
            CancellationToken.None);

        full.HasMore.Should().BeFalse();
        full.NextCursor.Should().BeNull();

        var oracle = full.Lines
            .OrderBy(l => l.PeriodUtc)
            .ThenBy(l => l.EntryId)
            .ToArray();

        oracle.Should().HaveCount(5);

        // Now read by keyset paging with small page size and ensure we get the exact same sequence.
        var seen = new List<GeneralJournalLine>();
        GeneralJournalCursor? cursor = null;

        while (true)
        {
            var page = await journal.GetPageAsync(
                new GeneralJournalPageRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    PageSize = 2,
                    Cursor = cursor
                },
                CancellationToken.None);

            page.Lines.Should().NotBeNull();
            page.Lines.Should().OnlyContain(l => l.PeriodUtc.Date == PeriodUtc.Date);

            // Ensure no duplicates by EntryId within accumulated.
            foreach (var line in page.Lines)
            {
                seen.Any(x => x.EntryId == line.EntryId).Should().BeFalse("keyset paging must not return duplicates");
                seen.Add(line);
            }

            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor!;
        }

        seen.Should().HaveCount(oracle.Length);
        seen.Select(x => x.EntryId).Should().BeEquivalentTo(oracle.Select(x => x.EntryId), o => o.WithStrictOrdering());
    }

    private static async Task SeedReportingCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);

                return existing.Account.Id;
            }

            return await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        // Required by tests
        await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        await GetOrCreateAsync("60", "Accounts Payable", AccountType.Liability);
        await GetOrCreateAsync("80", "Owner's Equity", AccountType.Equity);
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);
        await GetOrCreateAsync("91", "Expenses", AccountType.Expense);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "tests", ct: CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    periodUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount);
            },
            CancellationToken.None);
    }
}
