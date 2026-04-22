using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountCard_CurrencyOfOpeningBalanceTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Dec = new(2025, 12, 1);
    private static readonly DateOnly Jan = new(2026, 1, 1);

    [Fact]
    public async Task AccountCard_paging_opening_balance_is_currency_of_cursor_running_balance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Ensure required accounts exist.
        var (cashId, _, _) = await SeedCoAAsync(host);

        // Create a non-zero opening balance for Jan by posting in Dec and closing Dec.
        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc), "50", "80", 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(Dec, closedBy: "test", CancellationToken.None);
        }

        // Post many movements in Jan to force multiple pages.
        // Pattern: alternate Debit Cash (delta +1) and Credit Cash (delta -1).
        var deltas = new List<decimal>();

        for (var i = 0; i < 23; i++)
        {
            var dt = new DateTime(2026, 1, 2 + (i % 20), 9, i % 50, 0, DateTimeKind.Utc);

            if (i % 2 == 0)
            {
                // Debit Cash (+)
                await PostAsync(host, Guid.CreateVersion7(), dt, "50", "90.1", 1m);
                deltas.Add(+1m);
            }
            else
            {
                // Credit Cash (-)
                await PostAsync(host, Guid.CreateVersion7(), dt, "90.1", "50", 1m);
                deltas.Add(-1m);
            }
        }

        await using var readScope = host.Services.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var pageSize = 5;

        var allLines = new List<AccountCardReportLine>();
        AccountCardReportCursor? cursor = null;

        // First page opening comes from closed Dec balance.
        var expectedOpening = 100m;
        var expectedRunning = expectedOpening;

        decimal? expectedTotalDebit = null;
        decimal? expectedTotalCredit = null;

        while (true)
        {
            var page = await reader.GetPageAsync(
                new AccountCardReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = Jan,
                    ToInclusive = Jan,
                    PageSize = pageSize,
                    Cursor = cursor
                },
                CancellationToken.None);

            // Totals must be independent of paging (but we don't hardcode the amount here).
            expectedTotalDebit ??= page.TotalDebit;
            expectedTotalCredit ??= page.TotalCredit;
            page.TotalDebit.Should().Be(expectedTotalDebit.Value);
            page.TotalCredit.Should().Be(expectedTotalCredit.Value);

            // The page opening is the running balance right before the first line of this page.
            page.OpeningBalance.Should().Be(expectedOpening);

            foreach (var line in page.Lines)
            {
                expectedRunning += line.Delta;
                line.RunningBalance.Should().Be(expectedRunning);
                allLines.Add(line);
            }

            if (!page.HasMore)
            {
                page.NextCursor.Should().BeNull();
                // Closing balance must equal the last running balance we observed.
                page.ClosingBalance.Should().Be(expectedRunning);
                break;
            }

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;

            // Next page opening must equal last running balance of the previous page.
            expectedOpening = expectedRunning;
        }

        // Sanity: we fetched all expected lines.
        allLines.Should().HaveCount(deltas.Count);

        // Sanity: all lines are in requested month.
        allLines.Should().OnlyContain(l => AccountingPeriod.FromDateTime(l.PeriodUtc) == Jan);
    }

    [Fact]
    public async Task AccountCard_without_closed_snapshot_reconstructs_opening_balance_from_prior_history()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, equityId, revenueId) = await SeedCoAAsync(host);

        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), "50", "80", 100m);
        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc), "50", "90.1", 25m);
        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2026, 2, 18, 12, 0, 0, DateTimeKind.Utc), "90.1", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var page = await reader.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                PageSize = 50
            },
            CancellationToken.None);

        page.Lines.Should().BeEmpty();
        page.OpeningBalance.Should().Be(85m);
        page.TotalDebit.Should().Be(0m);
        page.TotalCredit.Should().Be(0m);
        page.ClosingBalance.Should().Be(85m);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    private static async Task<(Guid cashId, Guid equityId, Guid revenueId)> SeedCoAAsync(IHost host)
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
        var equityId = await GetOrCreateAsync("80", "Owner's Equity", AccountType.Equity);
        var revenueId = await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);

        return (cashId, equityId, revenueId);
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
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                var debit = coa.Get(debitCode);
                var credit = coa.Get(creditCode);
                ctx.Post(documentId, periodUtc, debit: debit, credit: credit, amount: amount);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            CancellationToken.None);
    }
}
