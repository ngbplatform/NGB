using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Posting;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// P3 coverage: cancellation token must stop platform operations and leave no partial writes.
/// Includes: cancellation before start, and cancellation while waiting for period lock (simulated by sleeping writer).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Cancellation_Propagation_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenCancellationAlreadyRequested_DoesNotWriteAnything()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var docId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(docId, periodUtc, chart.Get("50"), chart.Get("90.1"), 10m);
            }, manageTransaction: true, cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        await AssertNothingWrittenAsync(host, docId, PostingOperation.Post);
    }

    [Fact]
    public async Task Post_WhenCancelledWhileWaitingForLock_DoesNotWriteAnything()
    {
        await Fixture.ResetDatabaseAsync();

        // Sleep inside entry writer to hold the transaction+locks long enough.
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.Decorate<IAccountingEntryWriter, SleepBeforeWriteAccountingEntryWriter>();
            });

        await SeedMinimalCoaAsync(host);

        var month1 = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        // First post will sleep inside the writer while holding locks.
        var doc1 = Guid.CreateVersion7();
        var t1 = PostAsync(host, doc1, month1, 10m, CancellationToken.None);

        // Second post targets same period and should block on locks, then be cancelled.
        var doc2 = Guid.CreateVersion7();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Func<Task> act2 = async () => await PostAsync(host, doc2, month1, 11m, cts.Token);

        await act2.Should().ThrowAsync<OperationCanceledException>();

        // Let the first post finish cleanly.
        await t1;

        // First doc must exist.
        await AssertHasAtLeastOneEntryAsync(host, doc1);

        // Second doc must have no writes (entries + posting_log).
        await AssertNothingWrittenAsync(host, doc2, PostingOperation.Post);
    }

    private static async Task PostAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(PostingOperation.Post, async (ctx, innerCt) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(innerCt);
            ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, manageTransaction: true, ct);
    }

    private static async Task AssertNothingWrittenAsync(IHost host, Guid documentId, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().BeEmpty();

        var logPage = await sp.GetRequiredService<IPostingStateReader>().GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = documentId,
            Operation = operation,
            // Use wide time window: posting_log uses StartedAtUtc = UtcNow.
            FromUtc = DateTime.UtcNow.AddDays(-2),
            ToUtc = DateTime.UtcNow.AddDays(2),
            PageSize = 50,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty();
    }

    private static async Task AssertHasAtLeastOneEntryAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().NotBeEmpty();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private sealed class SleepBeforeWriteAccountingEntryWriter(
        IAccountingEntryWriter inner,
        IUnitOfWork uow)
        : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<NGB.Accounting.Registers.AccountingEntry> entries, CancellationToken ct = default)
        {
            // Sleep within the current transaction to keep the period lock held.
            await uow.EnsureConnectionOpenAsync(ct);

            uow.Transaction.Should().NotBeNull("sleep writer expects an active transaction");

            await using (var cmd = uow.Connection.CreateCommand())
            {
                cmd.CommandText = "select pg_sleep(3);";
                cmd.Transaction = uow.Transaction;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await inner.WriteAsync(entries, ct);
        }
    }
}
