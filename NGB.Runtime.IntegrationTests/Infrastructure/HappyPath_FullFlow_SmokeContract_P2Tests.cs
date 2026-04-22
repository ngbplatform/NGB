using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class HappyPath_FullFlow_SmokeContract_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FullUserFlow_Post_Unpost_Repost_CloseMonth_Reports_ShouldWork_EndToEnd()
    {
        await Fixture.ResetDatabaseAsync();

        var period = new DateOnly(2041, 5, 1);
        var dayUtc = new DateTime(2041, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedCoaAsync(host);

        var revenueDocId = Guid.CreateVersion7();
        var expenseDocId = Guid.CreateVersion7();

        // Owner invests 1,000 so cash stays non-negative after expenses.
        await PostAsync(host, Guid.CreateVersion7(), dayUtc, debit: "50", credit: "80", amount: 1000m);

        // Revenue 500
        await PostAsync(host, revenueDocId, dayUtc, debit: "50", credit: "90.1", amount: 500m);

        // Expense 120
        await PostAsync(host, expenseDocId, dayUtc, debit: "91", credit: "50", amount: 120m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tbReader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var bsReader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

            var tb = await tbReader.GetAsync(period, period, CancellationToken.None);
            tb.Should().NotBeEmpty();

            var bs = await bsReader.GetAsync(new BalanceSheetReportRequest
            {
                AsOfPeriod = period,
                IncludeNetIncomeInEquity = true,
                IncludeZeroAccounts = false
            });

            bs.IsBalanced.Should().BeTrue();
            bs.Difference.Should().Be(0m);
        }

        // Unpost revenue document
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(revenueDocId, CancellationToken.None);
        }

        // Repost expense to 200
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();
            await reposting.RepostAsync(
                expenseDocId,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(expenseDocId, dayUtc, chart.Get("91"), chart.Get("50"), 200m);
                },
                CancellationToken.None);
        }

        // Close month
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
        }

        // Reports should still work after closing, and closed period should be visible.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closedReader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();
            var tbReader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var bsReader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().ContainSingle(p => p.Period == period);

            var tb = await tbReader.GetAsync(period, period, CancellationToken.None);
            tb.Should().NotBeEmpty();

            var bs = await bsReader.GetAsync(new BalanceSheetReportRequest
            {
                AsOfPeriod = period,
                IncludeNetIncomeInEquity = true,
                IncludeZeroAccounts = false
            });

            bs.IsBalanced.Should().BeTrue();
            bs.Difference.Should().Be(0m);
        }
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "80",
            Name: "Owner's Equity",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debit,
        string credit,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, dateUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
