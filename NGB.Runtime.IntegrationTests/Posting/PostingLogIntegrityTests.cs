using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLogIntegrityTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_SameDocumentTwice_DoesNotDuplicatePostingLog_AndDoesNotChangeTimestamps()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var logWindow = PostingLogTestWindow.Capture();

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        await PostOnceAsync(host, documentId, period, amount: 100m);
        var first = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Post, logWindow);

        await PostOnceAsync(host, documentId, period, amount: 100m); // idempotent repeat
        var second = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Post, logWindow);

        second.Status.Should().Be(PostingStateStatus.Completed);
        second.StartedAtUtc.Should().Be(first.StartedAtUtc);
        second.CompletedAtUtc.Should().Be(first.CompletedAtUtc);
    }

    [Fact]
    public async Task UnpostAsync_SameDocumentTwice_DoesNotDuplicatePostingLog_AndDoesNotChangeTimestamps()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        var logWindow = PostingLogTestWindow.Capture();
        await UnpostOnceAsync(host, documentId);
        var first = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Unpost, logWindow);

        await UnpostOnceAsync(host, documentId); // idempotent repeat
        var second = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Unpost, logWindow);

        second.Status.Should().Be(PostingStateStatus.Completed);
        second.StartedAtUtc.Should().Be(first.StartedAtUtc);
        second.CompletedAtUtc.Should().Be(first.CompletedAtUtc);
    }

    [Fact]
    public async Task RepostAsync_SameDocumentTwice_DoesNotDuplicatePostingLog_AndDoesNotChangeTimestamps()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        var logWindow = PostingLogTestWindow.Capture();
        await RepostOnceAsync(host, documentId, period, amount: 200m);
        var first = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Repost, logWindow);

        await RepostOnceAsync(host, documentId, period, amount: 200m); // idempotent repeat
        var second = await ReadSinglePostingLogAsync(host, documentId, PostingOperation.Repost, logWindow);

        second.Status.Should().Be(PostingStateStatus.Completed);
        second.StartedAtUtc.Should().Be(first.StartedAtUtc);
        second.CompletedAtUtc.Should().Be(first.CompletedAtUtc);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount: amount);
            },
            ct: CancellationToken.None
        );
    }

    private static async Task UnpostOnceAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var unposting = sp.GetRequiredService<UnpostingService>();
        await unposting.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reposting = sp.GetRequiredService<RepostingService>();

        await reposting.RepostAsync(
            documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount: amount);
            },
            ct: CancellationToken.None
        );
    }

    private static async Task<PostingStateRecord> ReadSinglePostingLogAsync(
        IHost host,
        Guid documentId,
        PostingOperation operation,
        PostingLogTestWindow logWindow)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reader = sp.GetRequiredService<IPostingStateReader>();

        // Use a wide window: StartedAtUtc is the time of the operation, not the accounting period.
        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = documentId,
            Operation = operation,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1, "posting_log should have exactly one record per (document, operation)");
        return page.Records[0];
    }
}
