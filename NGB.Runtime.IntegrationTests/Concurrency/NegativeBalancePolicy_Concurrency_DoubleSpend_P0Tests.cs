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
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

/// <summary>
/// P0: NegativeBalancePolicy must be concurrency-safe.
/// Two concurrent "spend" postings should never both succeed if the second would drive balance below zero.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NegativeBalancePolicy_Concurrency_DoubleSpend_P0Tests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";
    private const string Expense = "90.2";

    [Fact]
    public async Task PostAsync_TwoConcurrentSpends_OnlyOneSucceeds_WhenCashPolicyForbidsNegative()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await SeedCoaAsync(host);

        var period = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        // Seed initial +100 cash (debit Cash / credit Revenue)
        var seedDocId = Guid.CreateVersion7();
        await PostAsync(host, seedDocId, period, amount: 100m, debitCode: Cash, creditCode: Revenue);

        // Two concurrent spends of 80 (debit Expense / credit Cash)
        var spend1 = Guid.CreateVersion7();
        var spend2 = Guid.CreateVersion7();

        using var barrier = new Barrier(participantCount: 2);

        var t1 = Task.Run(() => TryPostSpendAsync(host, spend1, period, barrier));
        var t2 = Task.Run(() => TryPostSpendAsync(host, spend2, period, barrier));

        var results = await Task.WhenAll(t1, t2);

        var successes = results.Count(r => r is null);
        var failures = results.Where(r => r is not null).ToArray();

        successes.Should().Be(1, "period advisory locks + operational balance must prevent double-spend");
        failures.Should().HaveCount(1);
        failures[0]!.Should().BeOfType<AccountingNegativeBalanceForbiddenException>();
        failures[0]!.Message.Should().Contain("Negative balance projected")
            .And.Contain("policy=Forbid");

        // Assert persisted state: exactly one spend is posted.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries1 = await entryReader.GetByDocumentAsync(spend1, CancellationToken.None);
        var entries2 = await entryReader.GetByDocumentAsync(spend2, CancellationToken.None);
        (entries1.Count + entries2.Count).Should().Be(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var month = AccountingPeriod.FromDateTime(period);
        var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);

        var cash = turnovers.Should().ContainSingle(x => x.AccountCode == Cash).Which;
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(80m);

        var expense = turnovers.Should().ContainSingle(x => x.AccountCode == Expense).Which;
        expense.DebitAmount.Should().Be(80m);
        expense.CreditAmount.Should().Be(0m);

        var revenue = turnovers.Should().ContainSingle(x => x.AccountCode == Revenue).Which;
        revenue.DebitAmount.Should().Be(0m);
        revenue.CreditAmount.Should().Be(100m);

        // Posting log: spend docs should have either Completed (success) or no record (rollback on failure).
        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            Operation = PostingOperation.Post,
            PageSize = 50
        }, CancellationToken.None);

        var spendLogs = page.Records.Where(r => r.DocumentId == spend1 || r.DocumentId == spend2).ToList();
        spendLogs.Should().HaveCount(1);
        spendLogs[0].Status.Should().Be(PostingStateStatus.Completed);
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Expense,
            "Expense",
            AccountType.Expense,
            StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime period,
        decimal amount,
        string debitCode,
        string creditCode)
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

    private static async Task<Exception?> TryPostSpendAsync(IHost host, Guid documentId, DateTime period, Barrier barrier)
    {
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, period, chart.Get(Expense), chart.Get(Cash), 80m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
