using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Definitions;
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
public sealed class PostingOperationsVsCloseMonthConcurrencyTests(PostgresTestFixture fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task PostVsCloseMonth_Concurrent_EndsClosed_AndPostEitherAppliedOrRejectedWithoutSideEffects()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var period = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var postTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);
        });

        var closeTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
        });

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(postTask, closeTask).WaitAsync(TimeSpan.FromSeconds(30));

        // CloseMonth must succeed; Post may fail if CloseMonth wins the race and closes the period first.
        outcomes[1].Error.Should().BeNull("CloseMonthAsync should succeed and close the period");
        (outcomes[0].Error is null || outcomes[0].Error is NgbException || outcomes[0].Error is PostingPeriodClosedException).Should().BeTrue();

        await AssertPeriodClosedAsync(host, period);

        // Verify side effects
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entries = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var docEntries = await entries.GetByDocumentAsync(documentId, CancellationToken.None);

            var fromUtc = DateTime.UtcNow.AddHours(-2);
            var toUtc = DateTime.UtcNow.AddHours(2);

            // Posting log rows for Post should exist IFF the post succeeded.
            var postLog = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 20
            }, CancellationToken.None);

            if (outcomes[0].Error is null)
            {
                docEntries.Should().HaveCount(1);
                postLog.Records.Should().HaveCount(1);
                postLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
            }
            else
            {
                // Closed-period guard must be "no side effects"
                docEntries.Should().BeEmpty();
                postLog.Records.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task RepostVsCloseMonth_Concurrent_EndsClosed_AndRepostEitherAppliedOrRejectedWithoutSideEffects()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var period = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);

            // Initial post 100
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var repostTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            // Repost changes amount to 200
            await docs.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 200m);
                },
                ct: CancellationToken.None);
        });

        var closeTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
        });

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(repostTask, closeTask).WaitAsync(TimeSpan.FromSeconds(30));

        outcomes[1].Error.Should().BeNull("CloseMonthAsync should succeed and close the period");
        (outcomes[0].Error is null || outcomes[0].Error is NgbException || outcomes[0].Error is PostingPeriodClosedException).Should().BeTrue();

        await AssertPeriodClosedAsync(host, period);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entries = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var docEntries = await entries.GetByDocumentAsync(documentId, CancellationToken.None);

            var fromUtc = DateTime.UtcNow.AddHours(-2);
            var toUtc = DateTime.UtcNow.AddHours(2);

            var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                DocumentId = documentId,
                Operation = PostingOperation.Repost,
                PageSize = 20
            }, CancellationToken.None);

            if (outcomes[0].Error is null)
            {
                // original + storno + new
                docEntries.Should().HaveCount(3);
                docEntries.Count(e => e.IsStorno).Should().Be(1);
                repostLog.Records.Should().HaveCount(1);
                repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
            }
            else
            {
                // Repost rejected in closed period => no new effects beyond initial post
                docEntries.Should().HaveCount(1);
                docEntries.Count(e => e.IsStorno).Should().Be(0);
                repostLog.Records.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task UnpostVsCloseMonth_Concurrent_EndsClosed_AndUnpostEitherAppliedOrRejectedWithoutSideEffects()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var period = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);

            // Initial post 100
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var unpostTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await docs.UnpostAsync(documentId, CancellationToken.None);
        });

        var closeTask = RunAsync(gate, async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
        });

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(unpostTask, closeTask).WaitAsync(TimeSpan.FromSeconds(30));

        outcomes[1].Error.Should().BeNull("CloseMonthAsync should succeed and close the period");
        (outcomes[0].Error is null || outcomes[0].Error is NgbException || outcomes[0].Error is PostingPeriodClosedException).Should().BeTrue();

        await AssertPeriodClosedAsync(host, period);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entries = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var docEntries = await entries.GetByDocumentAsync(documentId, CancellationToken.None);

            var fromUtc = DateTime.UtcNow.AddHours(-2);
            var toUtc = DateTime.UtcNow.AddHours(2);

            var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                DocumentId = documentId,
                Operation = PostingOperation.Unpost,
                PageSize = 20
            }, CancellationToken.None);

            if (outcomes[0].Error is null)
            {
                // original + storno
                docEntries.Should().HaveCount(2);
                docEntries.Count(e => e.IsStorno).Should().Be(1);
                unpostLog.Records.Should().HaveCount(1);
                unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
            }
            else
            {
                // Unpost rejected in closed period => document stays posted with only original entry
                docEntries.Should().HaveCount(1);
                docEntries.Count(e => e.IsStorno).Should().Be(0);
                unpostLog.Records.Should().BeEmpty();
            }
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

    private static async Task AssertPeriodClosedAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closingReader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();

        var periods = await closingReader.GetClosedAsync(period, period, CancellationToken.None);
        periods.Should().ContainSingle(p => p.Period == period);
    }

    private static async Task<Outcome> RunAsync(TaskCompletionSource<bool> gate, Func<Task> action)
    {
        await gate.Task;
        try
        {
            await action();
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}
