using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
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
public sealed class MultiPeriodRepostVsCloseMonthConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MultiPeriodRepostVsCloseMonth_Jan_Concurrent_NoPartialWrites_AndMonthEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;

        // Create a posted document with TWO entries in the SAME UTC day (Jan 1).
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

                    // Two postings on the same UTC day (valid by invariant).
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 10m);
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 20m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: race Repost vs CloseMonth for the same month.
        var closeTask = RunCloseMonthOutcomeAsync(host, janStart, gate);
        var repostTask = RunRepostOutcomeAsync(host, documentId, janUtc, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, repostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];

        // Assert: no deadlock; month ends closed.
        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // CloseMonth outcome: success or PeriodAlreadyClosed (depending on ordering).
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth error: {closeOutcome.Error}");

        // Repost outcome: success or NgbException/PostingPeriodClosedException (typically: period is closed / already in progress / already closed).
        (repostOutcome.Error is null || repostOutcome.Error is NgbException || repostOutcome.Error is PostingPeriodClosedException)
            .Should().BeTrue($"unexpected Repost error: {repostOutcome.Error}");

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        var stornoCount = entries.Count(e => e.IsStorno);

        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = documentId,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        if (repostOutcome.Error is null)
        {
            // Repost succeeded: orig(2) + storno(2) + new(2)
            entries.Should().HaveCount(6);
            stornoCount.Should().Be(2);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            // Repost rejected (period closed or raced): no partial writes, no posting_log row for Repost.
            entries.Should().HaveCount(2);
            stornoCount.Should().Be(0);

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

    private static async Task<Outcome> RunRepostOutcomeAsync(IHost host, Guid documentId, DateTime janUtc, TaskCompletionSource<bool> gate)
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

                    // New postings on the same UTC day (valid by invariant).
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 30m);
                    ctx.Post(documentId, janUtc, chart.Get("50"), chart.Get("90.1"), 40m);
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
