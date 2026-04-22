using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

[Collection(PostgresCollection.Name)]
public sealed class PostingCancellationTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_WhenCancelledInsideEntryWrite_RollsBackEverything_AndDoesNotLeavePostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        var probe = new CancellationProbe();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingEntryWriter>();
                services.AddSingleton(probe);
                services.AddScoped<IAccountingEntryWriter>(sp =>
                    new CancellingEntryWriter(
                        sp.GetRequiredService<CancellationProbe>()
                    ));
            });

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        using var cts = new CancellationTokenSource();

        // Act: START posting BEFORE we wait for the probe (otherwise the probe will never be reached).
        var postTask = PostOnceAsync(host, documentId, period, cts.Token);

        // Wait until the entry writer is reached (i.e., we're already inside the transaction, after posting_log begin).
        // IMPORTANT: don't pass the same CTS token here, because we want the wait to be independent from cancellation.
        await probe.EntryWriterReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Cancel while inside the writer.
        await cts.CancelAsync();

        // Assert (exception)
        await postTask.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();

        // Assert (no side effects)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            (await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None)).Should().BeEmpty();

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-7),
                ToUtc = DateTime.UtcNow.AddDays(7),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 50,
            }, CancellationToken.None);

            page.Records.Should().BeEmpty("posting_log must rollback with the transaction");
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            "50",
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            "90.1",
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct2) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct2);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount: 100m);
            },
            ct: ct);
    }

    private sealed class CancellationProbe
    {
        public TaskCompletionSource EntryWriterReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CancellingEntryWriter(CancellationProbe probe) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default)
        {
            probe.EntryWriterReached.TrySetResult();

            // Block until canceled (we want cancellation to happen inside the DB transaction).
            await Task.Delay(TimeSpan.FromMinutes(5), ct);

            // Not reached.
        }
    }
}
