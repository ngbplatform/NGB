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
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class PostingLogStaleInProgressRecoveryCloseFiscalYearTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_StaleInProgressPostingLog_AllowsSafeTakeover_AndCompletes()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Ensure CloseFiscalYear is not a no-op (we want real register writes).
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m);
        await PostExpenseAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 40m);

        var closeDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Simulate a crashed process that started fiscal-year close long ago (completed_at_utc is NULL).
        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await InsertInProgressPostingLogRowAsync(
            fixture.ConnectionString,
            closeDocumentId,
            PostingOperation.CloseFiscalYear,
            staleStartedAtUtc);

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(closeDocumentId, CancellationToken.None);
        entries.Should().HaveCount(2);

        var record = await ReadSinglePostingLogAsync(sp, closeDocumentId, PostingOperation.CloseFiscalYear);
        record.Status.Should().Be(PostingStateStatus.Completed);
        record.CompletedAtUtc.Should().NotBeNull();
        record.StartedAtUtc.Should().BeAfter(staleStartedAtUtc, "stale in-progress record must be taken over by updating started_at_utc");
    }

    [Fact]
    public async Task CloseFiscalYearAsync_RecentInProgressPostingLog_Throws_InProgress_AndDoesNotWriteClosingEntries()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Ensure CloseFiscalYear is not a no-op.
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m);

        var closeDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        var startedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await InsertInProgressPostingLogRowAsync(
            fixture.ConnectionString,
            closeDocumentId,
            PostingOperation.CloseFiscalYear,
            startedAtUtc);

        Func<Task> act = () => CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        await act.Should().ThrowAsync<FiscalYearClosingAlreadyInProgressException>()
            .WithMessage("*already in progress*endPeriod=2026-01-01*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(closeDocumentId, CancellationToken.None);
        entries.Should().BeEmpty("fiscal-year close must not write entries when posting_log indicates InProgress");

        var record = await ReadSinglePostingLogAsync(sp, closeDocumentId, PostingOperation.CloseFiscalYear);
        record.Status.Should().Be(PostingStateStatus.InProgress);
        record.StartedAtUtc.Should().BeCloseTo(startedAtUtc, precision: TimeSpan.FromSeconds(5));
        record.CompletedAtUtc.Should().BeNull();
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        // Fault-injection setup for integration tests.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           INSERT INTO accounting_posting_state(
                               document_id, operation, started_at_utc, completed_at_utc
                           )
                           VALUES (@document_id, @operation, @started_at_utc, NULL);
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<PostingStateRecord> ReadSinglePostingLogAsync(
        IServiceProvider sp,
        Guid documentId,
        PostingOperation operation)
    {
        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-6),
            ToUtc = DateTime.UtcNow.AddHours(6),
            DocumentId = documentId,
            Operation = operation,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        return page.Records[0];
    }

    private static async Task<Guid> SeedCoaForFiscalYearCloseAsync(IHost host)
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

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
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

        return retainedEarningsId;
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

    private static async Task PostExpenseAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("91");
                var credit = chart.Get("50");

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }
}
