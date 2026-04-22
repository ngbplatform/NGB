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
/// P0: PostingEngine negative-balance enforcement must interpret PreviousClosingBalance
/// from accounting_balances correctly for credit-normal accounts (including contra assets).
/// The balances table stores signed balance as (debit - credit), therefore a normal
/// credit balance is NEGATIVE and must be converted to a positive 'presented' balance
/// before comparing against NegativeBalancePolicy.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_NegativeBalance_OperationalBase_CreditNormalFromClosedBalance_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly ClosedPeriod = new(2025, 12, 1);
    private static readonly DateOnly Jan2026 = new(2026, 1, 1);
    private static readonly DateTime DayUtc = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_WhenClosedBalanceExists_ForCreditNormalAccount_NormalCreditPosting_MustNotBeRejected()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var contraId = await SeedContraScenarioAsync(host);

        // Seed: last closed period has contra closing balance = -100 (normal credit).
        await SeedClosedBalanceAsync(Fixture.ConnectionString, contraId, closingBalance: -100m);

        var docId = Guid.CreateVersion7();

        // Normal credit posting for a credit-normal account must INCREASE the presented balance.
        // Before the fix, base was treated as -100 and this posting was falsely rejected.
        await PostAsync(host, docId, DayUtc, debitCode: "91", creditCode: "02", amount: 10m);

        await using var scope = host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(docId, CancellationToken.None);

        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task PostAsync_WhenClosedBalanceExists_ForCreditNormalAccount_OppositeSideWithinBase_MustNotBeRejected()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var contraId = await SeedContraScenarioAsync(host);
        await SeedClosedBalanceAsync(Fixture.ConnectionString, contraId, closingBalance: -100m);

        // Debit the contra account by 90: this reduces presented balance from 100 to 10 (still >= 0).
        // Before the fix, the engine treated base as -100 and rejected even small debits.
        var docId = Guid.CreateVersion7();

        await PostAsync(host, docId, DayUtc, debitCode: "02", creditCode: "91", amount: 90m);

        await using var scope = host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(docId, CancellationToken.None);

        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task PostAsync_WhenClosedBalanceExists_ForCreditNormalAccount_OppositeSideExceedsBase_Forbid_ShouldThrow_AndWriteNothing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var contraId = await SeedContraScenarioAsync(host);
        await SeedClosedBalanceAsync(Fixture.ConnectionString, contraId, closingBalance: -100m);

        var docId = Guid.CreateVersion7();

        // Debit the contra account by 150: this would flip the balance to a debit-balance (violation).
        Func<Task> act = () => PostAsync(host, docId, DayUtc, debitCode: "02", creditCode: "91", amount: 150m);

        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected:*02*policy=Forbid*period=2026-01-01*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingEntryReader>()
                .GetByDocumentAsync(docId, CancellationToken.None))
            .Should().BeEmpty("forbidden negative balance must rollback accounting entries");

        var logs = await sp.GetRequiredService<IPostingStateReader>()
            .GetPageAsync(new PostingStatePageRequest
            {
                DocumentId = docId,
                Operation = PostingOperation.Post,
                PageSize = 10,
                Cursor = null
            }, CancellationToken.None);

        logs.Records.Should().BeEmpty("posting_log must rollback on forbidden negative balance");
    }

    private static async Task<Guid> SeedContraScenarioAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Expense must be Allow so it doesn't participate in enforcement.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Depreciation Expense",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Contra asset (credit-normal) with Forbid.
        return await accounts.CreateAsync(new CreateAccountRequest(
            Code: "02",
            Name: "Accumulated Depreciation",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            IsContra: true,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid
        ), CancellationToken.None);
    }

    private static async Task SeedClosedBalanceAsync(string cs, Guid accountId, decimal closingBalance)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // IMPORTANT:
        // With defense-in-depth closed-period triggers, we must seed balances FIRST
        // and only then insert accounting_closed_periods.
        await conn.ExecuteAsync(
            "INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance) VALUES (@P, @A, @S, 0, @C);",
            new { P = ClosedPeriod, A = accountId, S = Guid.Empty, C = closingBalance });

        await conn.ExecuteAsync(
            "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@P, @At, 'it');",
            new { P = ClosedPeriod, At = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc) });

        // Sanity: ensure AccountingPeriod math aligns with our test day.
        AccountingPeriod.FromDateTime(DayUtc).Should().Be(Jan2026);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
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
                ctx.Post(documentId, dateUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
