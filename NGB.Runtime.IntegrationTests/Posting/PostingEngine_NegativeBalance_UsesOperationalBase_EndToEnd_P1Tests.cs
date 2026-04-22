using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounting;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P1: PostingEngine must enforce NegativeBalancePolicy using the operational base:
///   base = latest closed closing balance (<= month) + current month turnovers (to-date).
/// This test is end-to-end and ensures PostingEngine does not ignore to-date turnovers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_NegativeBalance_UsesOperationalBase_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Expense = "90.2";

    [Fact]
    public async Task PostAsync_WhenToDateTurnoversAlreadyReducedCash_MustRejectNextSpend_BasedOnOperationalBase()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid cashId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            cashId = await accounts.CreateAsync(new CreateAccountRequest(
                Cash,
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
                CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Expense,
                "Expense",
                AccountType.Expense,
                StatementSection.Expenses,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        // Seed: last closed period (2025-12) had Cash closing balance = 100.
        var closedPeriod = new DateOnly(2025, 12, 1);
        var jan = new DateOnly(2026, 1, 1);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // IMPORTANT:
            // With defense-in-depth closed-period triggers, we must seed balances FIRST
            // and only then insert accounting_closed_periods.
            await conn.ExecuteAsync(
                "INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance) VALUES (@P, @A, @S, 0, @C);",
                new { P = closedPeriod, A = cashId, S = Guid.Empty, C = 100m });

            await conn.ExecuteAsync(
                "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@P, @At, 'it');",
                new { P = closedPeriod, At = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc) });
        }

        var day = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(day);
        month.Should().Be(jan);

        // First posting (spend 60): base=100, delta=-60 => projected=40 (ok).
        var doc1 = Guid.CreateVersion7();
        await PostAsync(host, doc1, day, debitCode: Expense, creditCode: Cash, amount: 60m);

        // Second posting (spend 50): engine MUST include to-date turnover (-60),
        // base becomes 40, delta=-50 => projected=-10 => forbid.
        var doc2 = Guid.CreateVersion7();

        Func<Task> act = () => PostAsync(host, doc2, day, debitCode: Expense, creditCode: Cash, amount: 50m);

        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected*50*policy=Forbid*period=2026-01-01*");

        // Assert: doc2 produced no side effects.
        await AssertNoSideEffectsAsync(host, doc2, month);

        // And doc1 did persist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var entries1 = await entryReader.GetByDocumentAsync(doc1, CancellationToken.None);
            entries1.Should().HaveCount(1);

            var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);

            var cash = turnovers.Should().ContainSingle(t => t.AccountCode == Cash).Which;
            cash.DebitAmount.Should().Be(0m);
            cash.CreditAmount.Should().Be(60m);
        }
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime period,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var logReader = sp.GetRequiredService<IPostingStateReader>();

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        // Turnovers table is aggregated per period, so we assert via posting log instead of attempting
        // to derive per-document turnover side effects.
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10,
            Cursor = null
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("failed posting must rollback posting_log row");

        // Sanity: turnovers for the month are not empty due to doc1.
        var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);
        turnovers.Should().NotBeEmpty();
    }
}
