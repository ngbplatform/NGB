using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class PagingTorture_PageSize1_ThreeReaders_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task PageSize1_GeneralJournalReader_GeneralJournalReportReader_AccountCardPagedReportReader_NoDuplicates_StableCursors()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await SeedMinimalCoaAsync(host);

        var expectedTotal = 0m;
        const int n = 15;

        for (var i = 1; i <= n; i++)
        {
            var amount = i * 10m;
            expectedTotal += amount;
            await PostOnceAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "50", creditCode: "90.1", amount);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // 1) GeneralJournalReader paging (raw)
        var gjReader = sp.GetRequiredService<IGeneralJournalReader>();
        var rawIds = await ReadAllGeneralJournalIdsAsync(gjReader);
        rawIds.Should().HaveCount(n);
        rawIds.Distinct().Should().HaveCount(n);

        // 2) GeneralJournalReportReader paging (report)
        var gjReport = sp.GetRequiredService<IGeneralJournalReportReader>();
        var reportIds = await ReadAllGeneralJournalReportIdsAsync(gjReport);

        reportIds.Should().Equal(rawIds, "report reader must preserve the same keyset ordering as the raw reader");

        // 3) AccountCardPagedReportReader paging
        var cardReader = sp.GetRequiredService<IAccountCardEffectivePagedReportReader>();
        var card = await ReadAllAccountCardLinesAsync(cardReader, cashId);

        card.Lines.Should().HaveCount(n);
        card.Lines.Select(l => l.EntryId).Distinct().Should().HaveCount(n);
        card.OpeningBalance.Should().Be(0m);
        card.TotalDebit.Should().Be(expectedTotal);
        card.TotalCredit.Should().Be(0m);
        card.ClosingBalance.Should().Be(expectedTotal);

        // Account card must contain the same entry ids as general journal for our dataset (cash participates in every entry).
        card.Lines.Select(l => l.EntryId).Should().Equal(rawIds);

        // Running balance continuity (torture for HasMore/NextCursor + running state).
        var running = card.OpeningBalance;
        foreach (var line in card.Lines)
        {
            (line.DebitAmount + line.CreditAmount).Should().BeGreaterThan(0m);
            line.CreditAmount.Should().Be(0m);
            line.CounterAccountCode.Should().Be("90.1");
            line.RunningBalance.Should().Be(running + line.Delta);
            running = line.RunningBalance;
        }
        running.Should().Be(card.ClosingBalance);
    }

    private static async Task<List<long>> ReadAllGeneralJournalIdsAsync(IGeneralJournalReader reader)
    {
        var ids = new List<long>();
        GeneralJournalCursor? cursor = null;

        // Hard guard to avoid infinite loops on cursor bugs.
        for (var i = 0; i < 10_000; i++)
        {
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1,
                Cursor = cursor
            }, CancellationToken.None);

            page.Lines.Should().HaveCountLessThanOrEqualTo(1);
            ids.AddRange(page.Lines.Select(l => l.EntryId));

            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;
        }

        return ids;
    }

    private static async Task<List<long>> ReadAllGeneralJournalReportIdsAsync(IGeneralJournalReportReader reader)
    {
        var ids = new List<long>();
        GeneralJournalCursor? cursor = null;

        for (var i = 0; i < 10_000; i++)
        {
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1,
                Cursor = cursor
            }, CancellationToken.None);

            page.Lines.Should().HaveCountLessThanOrEqualTo(1);
            ids.AddRange(page.Lines.Select(l => l.EntryId));

            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;
        }

        return ids;
    }

    private static async Task<AccountCardReportPage> ReadAllAccountCardLinesAsync(IAccountCardEffectivePagedReportReader reader, Guid accountId)
    {
        var all = new List<AccountCardReportLine>();
        AccountCardReportCursor? cursor = null;
        decimal opening = 0m;
        decimal closing = 0m;
        decimal running = 0m;
        var seenAny = false;

        decimal? expectedTotalDebit = null;
        decimal? expectedTotalCredit = null;
        decimal? expectedClosing = null;

        for (var i = 0; i < 10_000; i++)
        {
            var page = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1,
                Cursor = cursor
            }, CancellationToken.None);

            // Page size 1 torture
            page.Lines.Should().HaveCountLessThanOrEqualTo(1);

            // Totals and closing balance must be GRAND totals (independent of cursor).
            expectedTotalDebit ??= page.TotalDebit;
            expectedTotalCredit ??= page.TotalCredit;
            expectedClosing ??= page.ClosingBalance;

            page.TotalDebit.Should().Be(expectedTotalDebit.Value);
            page.TotalCredit.Should().Be(expectedTotalCredit.Value);
            page.ClosingBalance.Should().Be(expectedClosing.Value);

            if (!seenAny)
            {
                opening = page.OpeningBalance;
                running = opening;
                seenAny = true;
            }

            // Cursor-driven opening balance is the running balance right before the first line on this page.
            page.OpeningBalance.Should().Be(running);

            foreach (var l in page.Lines)
            {
                running += l.Delta;
                l.RunningBalance.Should().Be(running);
                all.Add(l);
            }

            closing = page.ClosingBalance;

            if (!page.HasMore)
            {
                closing.Should().Be(running);
                return new AccountCardReportPage
                {
                    AccountId = page.AccountId,
                    AccountCode = page.AccountCode,
                    FromInclusive = page.FromInclusive,
                    ToInclusive = page.ToInclusive,
                    OpeningBalance = opening,
                    TotalDebit = page.TotalDebit,
                    TotalCredit = page.TotalCredit,
                    ClosingBalance = closing,
                    Lines = all,
                    HasMore = false,
                    NextCursor = null
                };
            }

            page.NextCursor.Should().NotBeNull();
            page.NextCursor!.RunningBalance.Should().Be(running);
            cursor = page.NextCursor;
        }

        throw new XunitException("Cursor paging did not converge (possible bug in NextCursor/HasMore)");
    }

    private static async Task<Guid> SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return cashId;
    }

    private static async Task PostOnceAsync(
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
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            ct: CancellationToken.None);
    }
}
