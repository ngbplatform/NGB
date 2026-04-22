using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_MultiYearSequenceTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly YearStart = new(2025, 1, 1);
    private static readonly DateOnly YearEndPeriod = new(2025, 12, 1);

    [Fact]
    public async Task CloseFiscalYear_posts_closing_entries_and_is_idempotent_for_end_period()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var ids = await SeedCoaAsync(host);

        // Post some P&L activity across the fiscal year.
        // Revenue: +100 (cash debit, revenue credit)
        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc), "50", "90.1", 100m);

        // Expense: 40 (expense debit, cash credit)
        await PostAsync(host, Guid.CreateVersion7(), new DateTime(2025, 4, 10, 9, 0, 0, DateTimeKind.Utc), "91", "50", 40m);

        // Close all months BEFORE the fiscal year end month (strict rule enforced by PeriodClosingService).
        await CloseMonthsAsync(host, fromInclusive: YearStart, toInclusive: new DateOnly(2025, 11, 1));

        // Act: close fiscal year into the open end month (Dec 2025).
        await CloseFiscalYearAsync(host, YearEndPeriod, ids.RetainedEarningsAccountId);

        // Assert: TB for the whole year should show P&L accounts closed to zero and net moved to retained earnings.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tbReader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var rows = await tbReader.GetAsync(YearStart, YearEndPeriod, CancellationToken.None);

            var cash = rows.Single(r => r.AccountCode == "50");
            var revenue = rows.Single(r => r.AccountCode == "90.1");
            var expense = rows.Single(r => r.AccountCode == "91");
            var retained = rows.Single(r => r.AccountCode == "84");

            // Profit & loss accounts must be closed to zero after fiscal year close.
            revenue.ClosingBalance.Should().Be(0m);
            expense.ClosingBalance.Should().Be(0m);

            // Cash: +100 - 40 = +60 (debit-normal => signed closing is positive).
            cash.ClosingBalance.Should().Be(60m);

            // Retained earnings is credit-normal equity.
            // Signed balance is (debit - credit), so net credit 60 => ClosingBalance = -60.
            retained.ClosingBalance.Should().Be(-60m);
        }

        // Idempotency: closing the same end period again must fail as "already closed".
        var act = async () => await CloseFiscalYearAsync(host, YearEndPeriod, ids.RetainedEarningsAccountId);
        (await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>())
            .Which.Message.Should().Contain("already closed");
    }

    private sealed record CoaIds(Guid RetainedEarningsAccountId);

    private static async Task<CoaIds> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type, StatementSection section)
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
                    StatementSection: section,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        // Minimal set required for this test.
        await GetOrCreateAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await GetOrCreateAsync("91", "Expenses", AccountType.Expense, StatementSection.Expenses);
        var retained = await GetOrCreateAsync("84", "Retained Earnings", AccountType.Equity, StatementSection.Equity);

        return new CoaIds(retained);
    }

    private static async Task CloseMonthsAsync(IHost host, DateOnly fromInclusive, DateOnly toInclusive)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        for (var p = fromInclusive; p <= toInclusive; p = p.AddMonths(1))
            await closing.CloseMonthAsync(p, closedBy: "test", CancellationToken.None);
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly fiscalYearEndPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseFiscalYearAsync(fiscalYearEndPeriod, retainedEarningsAccountId, closedBy: "test", CancellationToken.None);
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
