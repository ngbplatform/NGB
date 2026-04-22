using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class LargeDataset_PagingCompleteness_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task GeneralJournal_RawAndReportPaging_ReturnSameEntryIds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoAAsync(host);

        // Arrange: 250 docs => 250 entries
        for (var i = 0; i < 250; i++)
        {
            await PostAsync(host, Guid.CreateVersion7(), amount: 10m + i);
        }

        var raw = await ReadAllGeneralJournalRawAsync(host);
        var report = await ReadAllGeneralJournalReportAsync(host);

        raw.EntryIds.Should().BeEquivalentTo(report);
    }

    [Fact]
    public async Task AccountCard_PagingLineReader_AndReportReader_AgreeOnLineCount_AndClosingBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await SeedCashRevenueCoAAsync(host);

        // Arrange: 300 docs, alternating debit/credit to keep closing deterministic
        decimal expectedClosing = 0m;

        for (var i = 0; i < 300; i++)
        {
            var amount = 1m;
            if (i % 2 == 0)
            {
                // Cash debit
                await PostAsync(host, Guid.CreateVersion7(), amount);
                expectedClosing += amount;
            }
            else
            {
                // Cash credit (Cash -> Revenue) by reversing posting direction
                await PostCashCreditAsync(host, Guid.CreateVersion7(), amount);
                expectedClosing -= amount;
            }
        }

        // Reader: keyset paging over lines
        var lines = await ReadAllAccountCardLinesAsync(host, cashId);

        // Report: running balance & totals
        await using var scope = host.Services.CreateAsyncScope();

        var report = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
            scope.ServiceProvider,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        report.Lines.Count.Should().Be(lines.Count);
        report.ClosingBalance.Should().Be(expectedClosing);

        // Stronger contract: line-reader paging and report-reader must agree on ordering + totals.
        lines.Select(l => l.EntryId).Should().Equal(report.Lines.Select(l => l.EntryId));

        var sumDebit = lines.Sum(l => l.DebitAmount);
        var sumCredit = lines.Sum(l => l.CreditAmount);
        var sumDelta = lines.Sum(l => l.Delta);

        report.OpeningBalance.Should().Be(0m, "fresh database has no closed balances");
        report.TotalDebit.Should().Be(sumDebit);
        report.TotalCredit.Should().Be(sumCredit);
        report.ClosingBalance.Should().Be(report.OpeningBalance + sumDebit - sumCredit);
        sumDelta.Should().Be(expectedClosing);
    }

    private static async Task SeedMinimalCoAAsync(IHost host)
    {
        await SeedCashRevenueCoAAsync(host);
    }

    private static async Task<Guid> SeedCashRevenueCoAAsync(IHost host)
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

        var cashId = await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);

        return cashId;
    }

    private static async Task PostAsync(IHost host, Guid documentId, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, PeriodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostCashCreditAsync(IHost host, Guid documentId, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                // credit cash: debit revenue, credit cash (still balanced, affects cash negatively)
                ctx.Post(documentId, PeriodUtc, chart.Get("90.1"), chart.Get("50"), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task<(HashSet<long> EntryIds, decimal TotalDebit, decimal TotalCredit)> ReadAllGeneralJournalRawAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        var ids = new HashSet<long>();
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        GeneralJournalCursor? cursor = null;

        while (true)
        {
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100,
                Cursor = cursor
            }, CancellationToken.None);

            foreach (var l in page.Lines)
            {
                ids.Add(l.EntryId);

                // Raw journal lines always represent a single posting amount. For this test we treat
                // debit/credit totals symmetrically as SUM(amount) across all lines in the filter.
                totalDebit += l.Amount;
                totalCredit += l.Amount;
            }

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        return (ids, totalDebit, totalCredit);
    }

    private static async Task<HashSet<long>> ReadAllGeneralJournalReportAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var ids = new HashSet<long>();

        GeneralJournalCursor? cursor = null;

        while (true)
        {
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100,
                Cursor = cursor
            }, CancellationToken.None);

            foreach (var l in page.Lines)
                ids.Add(l.EntryId);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        return ids;
    }

    private static async Task<List<AccountCardLine>> ReadAllAccountCardLinesAsync(IHost host, Guid accountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardPageReader>();

        var all = new List<AccountCardLine>();
        AccountCardLineCursor? cursor = null;

        while (true)
        {
            var page = await reader.GetPageAsync(new AccountCardLinePageRequest
            {
                AccountId = accountId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 150,
                Cursor = cursor
            }, CancellationToken.None);

            all.AddRange(page.Lines);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        return all;
    }
}
