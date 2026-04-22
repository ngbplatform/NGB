using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P0: CloseFiscalYear must preserve Trial Balance invariants.
/// ΣDebit == ΣCredit for the closed month, and ΣClosingBalance == 0.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_TrialBalanceBalanced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYear_AfterClosing_TrialBalanceIsBalanced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaAsync(host);

        // Revenue 100, Expense 40 => Net income 60.
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "50", credit: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "91", credit: "50", amount: 40m);

        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tbReader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var tb = await tbReader.GetAsync(endPeriod, endPeriod, CancellationToken.None);

            tb.Should().NotBeEmpty();

            tb.Sum(r => r.DebitAmount).Should().Be(tb.Sum(r => r.CreditAmount), "trial balance turnovers must be balanced");
            tb.Sum(r => r.ClosingBalance).Should().Be(0m, "trial balance balances must net to zero");

            // P&L closed to zero (explicit contract check).
            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "91" && r.ClosingBalance == 0m);
        }
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets));

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity));

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income));

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses));

        return retainedEarningsId;
    }

    private static async Task PostAsync(IHost host, Guid documentId, DateTime periodUtc, string debit, string credit, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, coa.Get(debit), coa.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
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
}
