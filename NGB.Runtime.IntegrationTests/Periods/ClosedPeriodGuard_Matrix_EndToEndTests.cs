using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Periods;
using NGB.Runtime.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodGuard_Matrix_EndToEndTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Theory]
    [InlineData(PostingOperation.Post)]
    [InlineData(PostingOperation.Unpost)]
    [InlineData(PostingOperation.Repost)]
    public async Task PostingEngine_AnyOperation_WhenPeriodClosed_FailsBeforeCreatingPostingLog(
        PostingOperation operation)
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var txDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(txDateUtc); // 2026-01-01

        await SeedMinimalCoaAsync(host);

        // We need an existing document for Unpost/Repost cases.
        // But for Post we must NOT reuse the same (documentId, Post) idempotency key,
        // otherwise PostingEngine will short-circuit as AlreadyCompleted *before* checking closed periods.
        var existingDocumentId = Guid.CreateVersion7();
        await PostOnceAsync(host, existingDocumentId, txDateUtc, amount: 100m);

        // For PostingOperation.Post we will use a different document id.
        var newDocumentId = Guid.CreateVersion7();

        // IMPORTANT:
        // This test focuses on PostingEngine closed-period guard.
        // We mark the period closed directly to avoid coupling to CloseMonth implementation details.
        await CloseMonthAsync(host, month);

        // Sanity: month must be marked closed in storage
        await AssertMonthIsClosedAsync(host, month);

        // Act
        Func<Task> act = operation switch
        {
            PostingOperation.Post => async () =>
            {
                await using var scopePosting = host.Services.CreateAsyncScope();
                var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

                // Sanity (same scope as PostingEngine): period must be closed
                var closedRepoInScope = scopePosting.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();
                (await closedRepoInScope.IsClosedAsync(month, CancellationToken.None)).Should()
                    .BeTrue("period must be closed before posting");

                await posting.PostAsync(
                    operation: PostingOperation.Post,
                    postingAction: async (ctx, ct) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(ct);
                        ctx.Post(newDocumentId, txDateUtc, chart.Get(Cash), chart.Get(Revenue), 10m);
                    },
                    manageTransaction: true,
                    ct: CancellationToken.None);
            },

            PostingOperation.Unpost => async () =>
            {
                await using var scopePosting = host.Services.CreateAsyncScope();
                var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

                // Sanity (same scope as PostingEngine): period must be closed
                var closedRepoInScope = scopePosting.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();
                (await closedRepoInScope.IsClosedAsync(month, CancellationToken.None)).Should()
                    .BeTrue("period must be closed before posting");

                await posting.PostAsync(
                    operation: PostingOperation.Unpost,
                    postingAction: async (ctx, ct) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(ct);
                        ctx.Post(existingDocumentId, txDateUtc, chart.Get(Cash), chart.Get(Revenue), 1m,
                            isStorno: true);
                        await Task.CompletedTask;
                    },
                    manageTransaction: true,
                    ct: CancellationToken.None);
            },

            PostingOperation.Repost => async () =>
            {
                await using var scopePosting = host.Services.CreateAsyncScope();
                var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

                // Sanity (same scope as PostingEngine): period must be closed
                var closedRepoInScope = scopePosting.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();
                (await closedRepoInScope.IsClosedAsync(month, CancellationToken.None)).Should()
                    .BeTrue("period must be closed before posting");

                await posting.PostAsync(
                    operation: PostingOperation.Repost,
                    postingAction: async (ctx, ct) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(ct);
                        ctx.Post(existingDocumentId, txDateUtc, chart.Get(Cash), chart.Get(Revenue), 2m);
                        await Task.CompletedTask;
                    },
                    manageTransaction: true,
                    ct: CancellationToken.None);
            },

            _ => throw new NgbArgumentOutOfRangeException(nameof(operation), operation, "Unexpected operation")
        };

        // Assert
        await act.Should().ThrowAsync<PostingPeriodClosedException>()
            .WithMessage("*Period is closed*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = operation == PostingOperation.Post ? newDocumentId : existingDocumentId,
            Operation = operation,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty("closed period guard must fail before creating posting_log");

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        // Existing posted entry must remain intact.
        var entries = await entryReader.GetByDocumentAsync(existingDocumentId, CancellationToken.None);
        entries.Should().HaveCount(1, "failed forbidden operation must not mutate existing posted entries");
        entries[0].Amount.Should().Be(100m);

        if (operation == PostingOperation.Post)
        {
            var entriesForNewDoc = await entryReader.GetByDocumentAsync(newDocumentId, CancellationToken.None);
            entriesForNewDoc.Should().BeEmpty("forbidden Post must not write any entries");
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }


    private static async Task CloseMonthAsync(IHost host, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(month, closedBy: "test", ct: CancellationToken.None);
    }

    private static async Task AssertMonthIsClosedAsync(IHost host, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closedRepo = scope.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();
        (await closedRepo.IsClosedAsync(month, CancellationToken.None)).Should()
            .BeTrue("month must be marked as closed before testing closed-period guard");
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
