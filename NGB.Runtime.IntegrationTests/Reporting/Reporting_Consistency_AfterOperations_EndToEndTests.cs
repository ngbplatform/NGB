using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P2 coverage: reporting must be consistent with ledger after platform operations.
/// We verify TrialBalance amounts (debits/credits/closing) match the underlying entries,
/// and remain consistent after Unpost.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Reporting_Consistency_AfterOperations_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TrialBalance_MatchesSumOfEntries_ThenAfterUnpostUpdatesConsistently()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var month = new DateOnly(2026, 1, 1);

        // Post 3 docs in the same month:
        // Cash Dr / Revenue Cr
        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();
        var d3 = Guid.CreateVersion7();

        await PostAsync(host, d1, new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc), 100m);
        await PostAsync(host, d2, new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc), 30m);
        await PostAsync(host, d3, new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc), 70m);

        // 1) TrialBalance must match sums from entries.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var tb = await sp.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(month, month, CancellationToken.None);

            tb.Should().NotBeEmpty();

            // Expected totals by design.
            var total = 200m;

            tb.Should().ContainSingle(r =>
                r.AccountCode == "50" &&
                r.DebitAmount == total &&
                r.CreditAmount == 0m &&
                r.OpeningBalance == 0m &&
                r.ClosingBalance == total);

            tb.Should().ContainSingle(r =>
                r.AccountCode == "90.1" &&
                r.DebitAmount == 0m &&
                r.CreditAmount == total &&
                r.OpeningBalance == 0m &&
                r.ClosingBalance == -total);

            // Cross-check against ledger: sum amounts in entries for each side.
            var entries = await ReadAllDocEntriesAsync(sp, [d1, d2, d3]);

            entries.Should().HaveCount(3);

            entries.Sum(e => e.Amount).Should().Be(total);

            var cashDebit = entries.Where(e => e.Debit.Code == "50").Sum(e => e.Amount);
            var cashCredit = entries.Where(e => e.Credit.Code == "50").Sum(e => e.Amount);
            cashDebit.Should().Be(total);
            cashCredit.Should().Be(0m);

            var revDebit = entries.Where(e => e.Debit.Code == "90.1").Sum(e => e.Amount);
            var revCredit = entries.Where(e => e.Credit.Code == "90.1").Sum(e => e.Amount);
            revDebit.Should().Be(0m);
            revCredit.Should().Be(total);
        }

        // 2) Unpost one document and ensure TB updates consistently (history remains, closing changes).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(d2, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var tb2 = await sp.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(month, month, CancellationToken.None);

            // After unposting d2 (30), net remains 170 because we still have d1 and d3 posted (and d2 offset to zero).
            // BUT: since unpost preserves history (original + storno), turnovers for accounts may include both sides.
            // The only invariant we enforce here is that ClosingBalance reflects the net effect of posted docs.
            tb2.Should().ContainSingle(r => r.AccountCode == "50" && r.ClosingBalance == 170m);
            tb2.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == -170m);

            // Ledger history for d2 must be original + storno.
            var d2Entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(d2, CancellationToken.None);
            d2Entries.Should().HaveCount(2);
            d2Entries.Count(e => e.IsStorno).Should().Be(1);

            // Net cash delta for d2 must be zero.
            var cashNet = d2Entries.Where(e => e.Debit.Code == "50" || e.Credit.Code == "50")
                .Sum(e => e.Debit.Code == "50" ? e.Amount : -e.Amount);
            cashNet.Should().Be(0m);
        }
    }

    private static async Task PostAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, manageTransaction: true, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<NGB.Accounting.Registers.AccountingEntry>> ReadAllDocEntriesAsync(IServiceProvider sp, IReadOnlyList<Guid> docIds)
    {
        var reader = sp.GetRequiredService<IAccountingEntryReader>();
        var all = new List<NGB.Accounting.Registers.AccountingEntry>();

        foreach (var id in docIds)
        {
            var entries = await reader.GetByDocumentAsync(id, CancellationToken.None);
            all.AddRange(entries);
        }

        return all;
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
