using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsProvider_CacheInvalidation_WithPostingAndReporting_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task NewScope_SeesCoAUpdates_AndCanPostAndReportWithNewAccount()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = CreateHost();

        // Keep the original scope alive for the entire test.
        // This is critical: the CoA provider caches a snapshot per scope, and the test
        // asserts that the old scope does NOT auto-refresh.
        await using var scope1 = host.Services.CreateAsyncScope();
        var sp1 = scope1.ServiceProvider;

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(period);

        {
            var accounts = sp1.GetRequiredService<IChartOfAccountsManagementService>();

            // Seed CoA: Cash + Revenue.
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid));

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow));

            // Prime provider snapshot in scope1.
            var provider1 = sp1.GetRequiredService<IChartOfAccountsProvider>();
            var chart1 = await provider1.GetAsync();
            chart1.TryGetByCode("51", out _).Should().BeFalse();

        }

        // In a new scope, add a new account 51.
        await using (var scope2 = host.Services.CreateAsyncScope())
        {
            var sp2 = scope2.ServiceProvider;

            var accounts = sp2.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "51",
                Name: "Bank",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid));

            // New scope must observe CoA changes.
            var provider2 = sp2.GetRequiredService<IChartOfAccountsProvider>();
            var chart2 = await provider2.GetAsync();
            chart2.Get("51").Code.Should().Be("51");

            // And we can post using the new account.
            var posting = sp2.GetRequiredService<PostingEngine>();
            var docId = Guid.CreateVersion7();

            await posting.PostAsync(
                PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docId, period, chart.Get("51"), chart.Get("90.1"), 100m);
                },
                manageTransaction: true,
                CancellationToken.None);

            var tb = await sp2.GetRequiredService<ITrialBalanceReader>().GetAsync(month, month, CancellationToken.None);

            tb.Should().ContainSingle(r => r.AccountCode == "51" && r.DebitAmount == 100m && r.CreditAmount == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.DebitAmount == 0m && r.CreditAmount == 100m);
        }

        // Assert: the OLD scope still has the old snapshot and cannot resolve the new account.
        Func<Task> actOldScope = async () =>
        {
            var posting = sp1.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    // Must fail because provider snapshot in this scope doesn't auto-refresh.
                    ctx.Post(Guid.CreateVersion7(), period, chart.Get("51"), chart.Get("90.1"), 1m);
                },
                manageTransaction: true,
                CancellationToken.None);
        };

        var ex = await actOldScope.Should().ThrowAsync<AccountNotFoundException>();
        ex.Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);
}
