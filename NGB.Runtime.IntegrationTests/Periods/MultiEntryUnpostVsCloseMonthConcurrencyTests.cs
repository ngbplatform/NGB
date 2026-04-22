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
using Xunit;
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonthVsUnpost_MultiEntry_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MultiEntryUnpostVsCloseMonth_Jan_Concurrent_NoPartialWrites_AndMonthEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;

        // Create a real document (Draft -> Posted) so Unpost has something to operate on.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);

            // Post 2 entries for the same document on the same UTC day (valid invariant).
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var income = chart.Get("90.1");

                    ctx.Post(documentId, janUtc, cash, income, 10m);
                    ctx.Post(documentId, janUtc, cash, income, 5m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var closeTask = RunCloseMonthOutcomeAsync(host, janStart, gate);
        var unpostTask = RunUnpostOutcomeAsync(host, documentId, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, unpostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var unpostOutcome = outcomes[1];

        // CloseMonth: either succeeds or sees already-closed (depending on timing).
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth error: {closeOutcome.Error}");

        // Unpost: either succeeds, or is rejected due to the period being closed.
        (unpostOutcome.Error is null || unpostOutcome.Error is PostingPeriodClosedException)
            .Should().BeTrue($"unexpected Unpost error: {unpostOutcome.Error}");

        // Assert
        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) January ends closed.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // 2) Entries must be either:
        //    - Unpost applied fully: orig(2) + storno(2) = 4 (storno=2)
        //    - Unpost rejected (period closed): orig(2) only (storno=0)
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        var stornoCount = entries.Count(e => e.IsStorno);

        if (entries.Count == 4)
        {
            stornoCount.Should().Be(2);

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-2),
                ToUtc = DateTime.UtcNow.AddHours(2),
                DocumentId = documentId,
                Operation = PostingOperation.Unpost,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            entries.Should().HaveCount(2);
            stornoCount.Should().Be(0);

            // If rejected, it must be due to the closed period.
            if (unpostOutcome.Error is PostingPeriodClosedException pce)
            {
                (pce.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                 pce.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
                    .Should().BeTrue($"expected closed-period rejection, got: {pce.Message}");
            }

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-2),
                ToUtc = DateTime.UtcNow.AddHours(2),
                DocumentId = documentId,
                Operation = PostingOperation.Unpost,
                PageSize = 20
            }, CancellationToken.None);

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

    private static async Task<Outcome> RunUnpostOutcomeAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.UnpostAsync(documentId, CancellationToken.None);
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
