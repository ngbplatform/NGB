using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P2 coverage: account card keyset pagination must be stable.
/// Fetching the next page with the same cursor should return the same lines even if new
/// entries are inserted *before* the cursor position.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Paging_Cursor_Stability_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AccountCard_Paging_IsStable_WhenNewEntriesInsertedBeforeCursor()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var month = new DateOnly(2026, 1, 1);
        var from = month;
        var to = month;

        Guid cashId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var chartProvider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await chartProvider.GetAsync(CancellationToken.None);
            cashId = chart.Get("50").Id;
        }

        // Create 5 postings so we have at least 3 pages with page size 2.
        // Periods are monotonically increasing.
        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            var docId = Guid.CreateVersion7();
            await PostCashRevenueAsync(host, docId, baseDay.AddDays(i), 10m + i);
        }

        AccountCardReportPage page1;
        AccountCardReportPage page2Before;

        // Fetch first page.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            page1 = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = from,
                ToInclusive = to,
                PageSize = 2,
                Cursor = null
            }, CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
        }

        // Fetch second page (baseline).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            page2Before = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = from,
                ToInclusive = to,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);

            page2Before.Lines.Should().HaveCount(2);
        }

        // Insert a new posting BEFORE the cursor position:
        // use an earlier PeriodUtc (day 1 of month).
        // This should not affect the composition of page 2 when using the same cursor.
        var newDocBeforeCursor = Guid.CreateVersion7();
        await PostCashRevenueAsync(host, newDocBeforeCursor, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 999m);

        // Fetch second page again with the SAME cursor.
        AccountCardReportPage page2After;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            page2After = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = from,
                ToInclusive = to,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);
        }

        // We compare by keyset identity: entry IDs should be identical in the second page.
        page2After.Lines.Select(l => l.EntryId).Should().Equal(page2Before.Lines.Select(l => l.EntryId));
        page2After.Lines.Select(l => l.DocumentId).Should().Equal(page2Before.Lines.Select(l => l.DocumentId));
    }

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, manageTransaction: true, CancellationToken.None);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }
}
