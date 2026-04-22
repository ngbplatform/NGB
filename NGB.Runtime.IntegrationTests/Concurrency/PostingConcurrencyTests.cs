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
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class PostingConcurrencyTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_SameDocumentConcurrently_TwoTasks_WritesExactlyOnce()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        using var barrier = new Barrier(participantCount: 2);

        // Act
        var t1 = Task.Run(() => PostOnceAsync(host, documentId, period, barrier));
        var t2 = Task.Run(() => PostOnceAsync(host, documentId, period, barrier));

        await Task.WhenAll(t1, t2);

        // Assert
        await AssertPostedExactlyOnceAsync(host, documentId, period);
    }

    [Fact]
    public async Task PostAsync_SameDocumentConcurrently_ThreeTasks_WritesExactlyOnce()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        using var barrier = new Barrier(participantCount: 3);

        // Act
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => PostOnceAsync(host, documentId, period, barrier)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        await AssertPostedExactlyOnceAsync(host, documentId, period);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash (Asset)
        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Revenue (Income)
        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, Barrier barrier)
    {
        // IMPORTANT: each PostAsync gets its own DI scope so PostgresUnitOfWork (IAsyncDisposable)
        // is disposed asynchronously and any transaction state cannot leak between calls.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        // Synchronize start to maximize race probability.
        barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var debit = chart.Get(Cash);
                var credit = chart.Get(Revenue);

                ctx.Post(
                    documentId,
                    period,
                    debit,
                    credit,
                    amount: 100m
                );
            },
            ct: CancellationToken.None
        );
    }

    private static async Task AssertPostedExactlyOnceAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(1);
        entries[0].Amount.Should().Be(100m);
        entries[0].Debit.Code.Should().Be(Cash);
        entries[0].Credit.Code.Should().Be(Revenue);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == Cash);
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(0m);

        var revenue = turnovers.Single(x => x.AccountCode == Revenue);
        revenue.DebitAmount.Should().Be(0m);
        revenue.CreditAmount.Should().Be(100m);

        // Posting log must contain exactly one record for this operation.
        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        page.Records[0].CompletedAtUtc.Should().NotBeNull();
    }
}
