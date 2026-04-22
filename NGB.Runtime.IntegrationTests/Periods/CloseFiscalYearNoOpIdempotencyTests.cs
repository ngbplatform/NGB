using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYearNoOpIdempotencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_NoPLMovements_CreatesPostingLogButNoEntries()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        // Convention: fiscalYearEndPeriod is the LAST month of the fiscal year (must be OPEN),
        // while all months BEFORE it must be closed.
        var fiscalYearEndPeriod = new DateOnly(2025, 12, 1);

        await SeedCoaAsync(host);

        // Create only balance-sheet activity in Dec 2025 (no Income/Expense) so FY close becomes a no-op.
        await PostBalanceSheetOnlyAsync(host, documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            amount: 1m);

        await CloseAllMonthsBeforeEndPeriod2025Async(host);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            // Retained earnings account is "300" (Equity).
            var retainedEarningsId = await GetAccountIdAsync(scope.ServiceProvider, "300");

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().BeEmpty("no P&L movements => FY close should be a no-op with no closing entries");

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-2),
                ToUtc = DateTime.UtcNow.AddHours(2),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CloseFiscalYearAsync_NoPLMovements_TwoConcurrentCalls_Idempotent_NoEntries_OnePostingLogRow()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var fiscalYearEndPeriod = new DateOnly(2025, 12, 1);

        await SeedCoaAsync(host);

        await PostBalanceSheetOnlyAsync(host, documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            amount: 1m);

        await CloseAllMonthsBeforeEndPeriod2025Async(host);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        Guid retainedEarningsId;
        await using (var scope = host.Services.CreateAsyncScope())
            retainedEarningsId = await GetAccountIdAsync(scope.ServiceProvider, "300");

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var tasks = new[]
        {
            RunCloseFiscalYearAsync(host, fiscalYearEndPeriod, retainedEarningsId, gate),
            RunCloseFiscalYearAsync(host, fiscalYearEndPeriod, retainedEarningsId, gate)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(45));

        // We allow either: one succeeds and one fails (in-progress / already closed), or both succeed via idempotency.
        outcomes.All(o => o.Error is null || o.Error is FiscalYearAlreadyClosedException || o.Error is FiscalYearClosingAlreadyInProgressException).Should().BeTrue();

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-2),
                ToUtc = DateTime.UtcNow.AddHours(2),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1, "idempotency must prevent duplicate CloseFiscalYear posting state rows");
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
    }

    private static async Task<Outcome> RunCloseFiscalYearAsync(
        IHost host,
        DateOnly fiscalYearEndPeriod,
        Guid retainedEarningsId,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Add P&L accounts too (zero movement), to reflect real CoA.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Guid> GetAccountIdAsync(IServiceProvider sp, string code)
    {
        var reader = sp.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await reader.GetAsync(CancellationToken.None);
        return chart.Get(code).Id;
    }

    private static async Task PostBalanceSheetOnlyAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                // Cash D / Retained earnings C
                ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("300"), amount);
            },
            ct: CancellationToken.None);
    }

    private static async Task CloseAllMonthsBeforeEndPeriod2025Async(IHost host)
    {
        // Close Jan..Nov 2025. End period (Dec) must remain OPEN for CloseFiscalYear.
        var start = new DateOnly(2025, 1, 1);

        for (var i = 0; i < 11; i++)
        {
            var month = start.AddMonths(i);
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(month, closedBy: "test", ct: CancellationToken.None);
        }
    }

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}
