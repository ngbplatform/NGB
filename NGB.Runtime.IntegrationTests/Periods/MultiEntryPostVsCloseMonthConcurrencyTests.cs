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
using Xunit;
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class MultiEntryPostVsCloseMonthConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MultiEntryPostVsCloseMonth_Jan_Concurrent_NoPartialWrites_AndMonthEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: CloseMonth vs multi-entry Post (same UTC day).
        var closeTask = RunCloseMonthOutcomeAsync(host, janStart, gate);
        var postTask = RunPostOutcomeAsync(host, documentId, janUtc, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var postOutcome = outcomes[1];

        // Assert: CloseMonth succeeds or is already closed (depending on race).
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth error: {closeOutcome.Error}");

        // Assert: Post either succeeds, or fails due to closed period (no partial writes).
        if (postOutcome.Error is not null)
        {
            postOutcome.Error.Should().BeOfType<PostingPeriodClosedException>();
            postOutcome.Error.Message.Should().Contain("closed", "expected closed-period rejection");
        }

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // Month ends closed.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // Posting log for Post exists only if the Post succeeded.
        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        // Entries must be all-or-nothing.
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        if (postOutcome.Error is null)
        {
            entries.Should().HaveCount(2);
            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();

            var doc = await docRepo.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
        }
        else
        {
            entries.Should().BeEmpty();
            page.Records.Should().BeEmpty();

            var doc = await docRepo.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft);
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

    private static async Task<Outcome> RunPostOutcomeAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Two entries, same UTC day (valid).
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 10m);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 5m);
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
