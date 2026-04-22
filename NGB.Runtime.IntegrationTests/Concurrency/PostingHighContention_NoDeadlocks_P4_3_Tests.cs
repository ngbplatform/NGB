using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class PostingHighContention_NoDeadlocks_P4_3_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostingEngine_ManyConcurrentPosts_SamePeriod_NoDeadlocks_AndSingleLogRecordPerDoc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        const int n = 30;
        var docs = Enumerable.Range(0, n).Select(_ => Guid.CreateVersion7()).ToArray();

        // Coordinated start to maximize lock contention (period lock, insert hot spots).
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var tasks = docs.Select(docId => Task.Run(async () =>
        {
            await start.Task;
            await PostAsync(host, docId, ReportingTestHelpers.Day15Utc, "50", "90.1", 1m, cts.Token);
        }, cts.Token)).ToArray();

        start.SetResult();

        await Task.WhenAll(tasks);

        // Assert: posting log contains exactly one Completed record per document.
        await using var scope = host.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var page = await log.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-2),
            ToUtc = DateTime.UtcNow.AddDays(2),
            Operation = PostingOperation.Post,
            PageSize = 1000
        }, CancellationToken.None);

        page.Records.Should().HaveCount(n);
        page.Records.Select(r => r.DocumentId).Should().BeEquivalentTo(docs);
        page.Records.All(r => r.Status == PostingStateStatus.Completed).Should().BeTrue();
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, innerCt) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(innerCt);
                ctx.Post(
                    documentId,
                    dateUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount);
            },
            ct);
    }
}
