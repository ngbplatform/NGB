using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class MultiEntryRepostVsCloseMonthConcurrency_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MultiEntryRepostVsCloseMonth_Jan_Concurrent_NoPartialWrites_AndMonthEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;

        // Create a real document (Draft -> Posted) with multiple entries, so Repost will produce storno+new multi-entry.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);

            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Two entries, same UTC day (valid).
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 10m);
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 5m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: CloseMonth vs multi-entry Repost (same UTC day).
        var closeTask = RunCloseMonthOutcomeAsync(host, janStart, gate);
        var repostTask = RunRepostOutcomeAsync(host, documentId, janUtc, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, repostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];

        // CloseMonth: either succeeds or sees already-closed (depending on timing).
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth error: {closeOutcome.Error}");

        // Repost: either succeeds, or is rejected due to the period being closed.
        (repostOutcome.Error is null || repostOutcome.Error is PostingPeriodClosedException || repostOutcome.Error is NgbException)
            .Should().BeTrue($"unexpected Repost error: {repostOutcome.Error}");

        // Assert
        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) January ends closed.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // 2) Entries must be all-or-nothing.
        //    - Repost applied fully: orig(2) + storno(2) + new(2) = 6 (storno=2)
        //    - Repost rejected (period closed): orig(2) only (storno=0)
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        var stornoCount = entries.Count(e => e.IsStorno);

        // Posting log page for Repost.
        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = documentId,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        var doc = await docRepo.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);

        if (entries.Count == 6)
        {
            stornoCount.Should().Be(2);

            // Very lightweight signal that both "storno" and "new" parts were written.
            entries.Where(e => e.IsStorno).Sum(e => e.Amount).Should().Be(15m);
            entries.Where(e => !e.IsStorno).Sum(e => e.Amount).Should().Be(10m + 5m + 20m + 15m);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            entries.Should().HaveCount(2);
            stornoCount.Should().Be(0);

            // If rejected, it must be due to the closed period.
            if (repostOutcome.Error is not null)
            {
                (repostOutcome.Error.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                 repostOutcome.Error.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
                    .Should().BeTrue($"expected closed-period rejection, got: {repostOutcome.Error.Message}");
            }

            page.Records.Should().BeEmpty();
        }
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
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Outcome> RunCloseMonthOutcomeAsync(IHost host, DateOnly period, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunRepostOutcomeAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Two new entries, same UTC day (valid).
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 20m);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 15m);
                },
                ct: CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private sealed record Outcome(Exception? Error)
    {
        public static Outcome Success() => new(Error: null);
        public static Outcome Fail(Exception ex) => new(Error: ex);
    }
}
