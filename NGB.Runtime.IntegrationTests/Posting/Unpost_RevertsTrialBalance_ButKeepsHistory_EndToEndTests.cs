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

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P1 coverage: Unpost must revert balances (trial balance) to the pre-post state,
/// while preserving the ledger history (original entry + storno entry).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Unpost_RevertsTrialBalance_ButKeepsHistory_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAsync_RevertsTrialBalanceToBaseline_ButEntriesContainOriginalAndStorno()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var docId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var month = new DateOnly(2026, 1, 1);

        // 0) Baseline TB must be empty.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb0 = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(month, month, CancellationToken.None);
            tb0.Should().BeEmpty();
        }

        // 1) Post document (Cash Dr 100, Revenue Cr 100)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(docId, period, chart.Get("50"), chart.Get("90.1"), 100m);
            }, manageTransaction: true, CancellationToken.None);
        }

        // TB after post.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb1 = await scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(month, month, CancellationToken.None);

            tb1.Should().ContainSingle(r => r.AccountCode == "50" && r.DebitAmount == 100m && r.CreditAmount == 0m);
            tb1.Should().ContainSingle(r => r.AccountCode == "90.1" && r.DebitAmount == 0m && r.CreditAmount == 100m);
        }

        // 2) Unpost
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(docId, CancellationToken.None);
        }

        // 3) TB after unpost must return to baseline net state (closing = 0), but history remains.
        //
        // Note: depending on implementation, TrialBalance may either:
        //   A) return an empty dataset when net is zero (no rows), or
        //   B) return rows with zero closing balance, but non-zero turnovers (since original + storno both occurred).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var tb2 = await sp.GetRequiredService<ITrialBalanceReader>().GetAsync(month, month, CancellationToken.None);

            if (tb2.Count == 0)
            {
                // OK: empty dataset semantics.
            }
            else
            {
                // OK: explicit rows semantics (net 0, turnovers preserved).
                tb2.Should().ContainSingle(r =>
                    r.AccountCode == "50"
                    && r.OpeningBalance == 0m
                    && r.ClosingBalance == 0m
                    && r.DebitAmount == 100m
                    && r.CreditAmount == 100m);

                tb2.Should().ContainSingle(r =>
                    r.AccountCode == "90.1"
                    && r.OpeningBalance == 0m
                    && r.ClosingBalance == 0m
                    && r.DebitAmount == 100m
                    && r.CreditAmount == 100m);

                tb2.Should().OnlyContain(r => r.OpeningBalance == 0m && r.ClosingBalance == 0m);
            }

            var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(2, "unpost must keep history as original + storno");
            entries.Count(e => e.IsStorno).Should().Be(1);

            // Net effect must be zero for Cash.
            var cashNet = entries.Where(e => e.Debit.Code == "50" || e.Credit.Code == "50")
                .Sum(e => e.Debit.Code == "50" ? e.Amount : -e.Amount);
            cashNet.Should().Be(0m);
        }
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
