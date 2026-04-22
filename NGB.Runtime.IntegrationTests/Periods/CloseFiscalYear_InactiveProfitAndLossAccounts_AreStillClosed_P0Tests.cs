using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_InactiveProfitAndLossAccounts_AreStillClosed_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_WhenProfitAndLossAccountWasDeactivated_AfterMovements_StillPostsClosingEntries_AndZerosItOut()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Keep the FY close simple: endPeriod is January => there are no prior months to close.
        var endPeriod = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var (retainedEarningsId, revenueId) = await SeedCoaAsync(host);

        // Revenue 100 (credit-normal). This produces a non-zero P&L balance that must be closed.
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m);

        // Now the edge case: deactivate the P&L account AFTER it has movements.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await mgmt.SetActiveAsync(revenueId, isActive: false, CancellationToken.None);
        }

        var closeDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(closeDocumentId, CancellationToken.None);
            entries.Should().ContainSingle(e =>
                e.Amount == 100m &&
                e.Debit.Code == "90.1" &&
                e.Credit.Code == "300");

            var tb = await trialBalance.GetAsync(endPeriod, endPeriod, CancellationToken.None);
            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "300" && r.ClosingBalance == -100m);

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-1),
                ToUtc = DateTime.UtcNow.AddHours(1),
                DocumentId = closeDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().ContainSingle(r =>
                r.DocumentId == closeDocumentId &&
                r.Operation == PostingOperation.CloseFiscalYear &&
                r.Status == PostingStateStatus.Completed);
        }
    }

    private static async Task<(Guid retainedEarningsId, Guid revenueId)> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        var revenueId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return (retainedEarningsId, revenueId);
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }

    private static async Task PostRevenueAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }
}
