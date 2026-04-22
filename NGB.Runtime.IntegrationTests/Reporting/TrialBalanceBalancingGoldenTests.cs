using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Reporting Core — golden tests for Trial Balance integrity & balancing invariants.
/// Uses far-future periods to avoid collisions with other tests.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TrialBalanceBalancingGoldenTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TrialBalance_IsBalanced_DebitEqualsCredit_ForPeriodRange()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var period = new DateOnly(2034, 1, 1);
        var periodUtc = Utc(period);

        await SeedReportingCoAAsync(sp);

        // A small realistic flow:
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "80", amount: 1000m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "91", credit: "60", amount: 200m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "60", credit: "50", amount: 50m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "90.1", amount: 500m);

        var tb = sp.GetRequiredService<ITrialBalanceReader>();

        // In the current codebase Trial Balance reader returns rows only (no totals wrapper).
        var rows = await tb.GetAsync(fromInclusive: period, toInclusive: period);

        // Core integrity: total turnovers must balance.
        rows.Sum(r => r.DebitAmount).Should().Be(rows.Sum(r => r.CreditAmount));

        // Sanity: we should have at least these accounts (Cash, AP, Equity, Revenue, Expenses)
        rows.Select(r => r.AccountCode).Should().Contain(new[] { "50", "60", "80", "90.1", "91" });

        // And each row must respect Closing = Opening + (Debit - Credit)
        foreach (var r in rows)
            r.ClosingBalance.Should().Be(r.OpeningBalance + (r.DebitAmount - r.CreditAmount));
    }

    private static DateTime Utc(DateOnly periodMonthStart) =>
        new(periodMonthStart.Year, periodMonthStart.Month, periodMonthStart.Day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task SeedReportingCoAAsync(IServiceProvider sp)
    {
        var repo = sp.GetRequiredService<IChartOfAccountsRepository>();
        var mgmt = sp.GetRequiredService<IChartOfAccountsManagementService>();

        static async Task EnsureAsync(
            IChartOfAccountsRepository repo,
            IChartOfAccountsManagementService mgmt,
            string code,
            string name,
            AccountType type,
            StatementSection section)
        {
            var all = await repo.GetForAdminAsync(includeDeleted: true);
            var existing = all.FirstOrDefault(a => a.Account.Code == code);

            if (existing is null)
            {
                var id = await mgmt.CreateAsync(new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    StatementSection: section,
                    IsContra: false));

                await mgmt.SetActiveAsync(id, true);
                return;
            }

            if (!existing.IsActive)
                await mgmt.SetActiveAsync(existing.Account.Id, true);
        }

        await EnsureAsync(repo, mgmt, "50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync(repo, mgmt, "60", "Accounts Payable", AccountType.Liability, StatementSection.Liabilities);
        await EnsureAsync(repo, mgmt, "80", "Owner's Equity", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync(repo, mgmt, "90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync(repo, mgmt, "91", "Expenses", AccountType.Expense, StatementSection.Expenses);
    }

    private static async Task PostAsync(
        IServiceProvider sp,
        Guid documentId,
        DateTime periodUtc,
        string debit,
        string credit,
        decimal amount)
    {
        var engine = sp.GetRequiredService<PostingEngine>();

        await engine.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, coa.Get(debit), coa.Get(credit), amount);
            },
            manageTransaction: true);
    }
}
