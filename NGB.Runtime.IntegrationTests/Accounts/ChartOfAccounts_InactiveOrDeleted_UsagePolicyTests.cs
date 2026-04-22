using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccounts_InactiveOrDeleted_UsagePolicyTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InactiveAccount_IsExcludedFromRuntimeChartSnapshot_AndCannotBeResolvedForPosting()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _) = await CreateCashAndRevenueAsync(host);

        // deactivate Cash
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.SetActiveAsync(cashId, isActive: false, CancellationToken.None);
        }

        // New scope => new snapshot => cash should not be present
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);

            Action act = () => chart.Get("50");
            act.Should().Throw<AccountNotFoundException>()
                .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
        }

        // Attempt to post using the runtime chart should fail early (no side effects).
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var post = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var cash = chart.Get("50"); // should throw
                var revenue = chart.Get("90.1");
                ctx.Post(documentId, period, cash, revenue, 100m);
            }, manageTransaction: true, CancellationToken.None);
        };

        var ex = await post.Should().ThrowAsync<AccountNotFoundException>();
        ex.Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");

        var month = AccountingPeriod.FromDateTime(period);
        await AssertNoSideEffectsAsync(host, documentId, month);
    }

    [Fact]
    public async Task InactiveAccount_RemainsVisibleInReportsForHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        var (cashId, _) = await CreateCashAndRevenueAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Deactivate after movements.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.SetActiveAsync(cashId, isActive: false, CancellationToken.None);
        }

        // Reports should still include the account based on registers/turnovers (inactive is not deleted).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var rows = await tb.GetAsync(month, month, CancellationToken.None);

            rows.Should().Contain(r => r.AccountCode == "50");
            rows.Should().Contain(r => r.AccountCode == "90.1");
        }
    }

    [Fact]
    public async Task DeletedAccount_IsExcludedFromRuntimeChartSnapshot()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid deletedId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            deletedId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "51",
                Name: "Bank",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
                CancellationToken.None);

            await accounts.MarkForDeletionAsync(deletedId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);

            Action act = () => chart.Get("51");
            act.Should().Throw<AccountNotFoundException>()
                .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
        }
    }

    private static async Task<(Guid CashId, Guid RevenueId)> CreateCashAndRevenueAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
            CancellationToken.None);

        var revenueId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            var cash = chart.Get("50");
            var revenue = chart.Get("90.1");
            ctx.Post(documentId, period, cash, revenue, amount);
        }, manageTransaction: true, CancellationToken.None);
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
        var logReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);
        turnovers.Should().BeEmpty();

        var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10,
            Cursor = null},
            CancellationToken.None);

        page.Records.Should().BeEmpty();
    }
}
