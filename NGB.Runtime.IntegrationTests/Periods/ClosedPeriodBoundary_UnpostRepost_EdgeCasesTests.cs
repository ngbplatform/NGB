using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodBoundary_UnpostRepost_EdgeCasesTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task ClosingJanuary_DoesNotBlock_UnpostOrRepost_ForDocumentAtFeb01MidnightUtc()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var jan = new DateOnly(2026, 1, 1);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // Close January, but operate on a document that belongs to February.
        await CloseMonthAsync(host, jan);

        // Unpost should be allowed (storno is written into Feb).
        var docUnpost = Guid.CreateVersion7();
        await PostOnceAsync(host, docUnpost, febUtc, amount: 100m);

        await UnpostAsync(host, docUnpost);

        var entriesAfterUnpost = await GetEntriesAsync(host, docUnpost);
        entriesAfterUnpost.Should().HaveCount(2);
        entriesAfterUnpost.Count(e => e.IsStorno).Should().Be(1);
        entriesAfterUnpost.All(e => e.Period == febUtc).Should().BeTrue();

        // Repost should also be allowed (storno+new are written into Feb).
        var docRepost = Guid.CreateVersion7();
        await PostOnceAsync(host, docRepost, febUtc, amount: 200m);

        await RepostAsync(host, docRepost, febUtc, amount: 200m);

        var entriesAfterRepost = await GetEntriesAsync(host, docRepost);
        entriesAfterRepost.Should().HaveCount(3);
        entriesAfterRepost.Count(e => e.IsStorno).Should().Be(1);
        entriesAfterRepost.All(e => e.Period == febUtc).Should().BeTrue();
    }

    [Fact]
    public async Task Repost_OriginalPeriodClosed_Throws_AndDoesNotWriteAnything()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var logWindow = PostingLogTestWindow.Capture();

        await SeedMinimalCoaAsync(host);

        var jan = new DateOnly(2026, 1, 1);
        var janLastMomentUtc = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);

        var documentId = Guid.CreateVersion7();

        await PostOnceAsync(host, documentId, janLastMomentUtc, amount: 100m);
        await CloseMonthAsync(host, jan);

        var entriesBefore = await GetEntriesAsync(host, documentId);

        // Act: attempt to repost inside January (same UTC day), but January is already closed.
        // Note: moving the new posting to another UTC day/month is forbidden by an invariant
        // (all entries within a single posting operation must belong to the same UTC day).
        Func<Task> act = async () => await RepostAsync(host, documentId, janLastMomentUtc, amount: 100m);

        // Assert: forbidden because storno and new posting must be written into the ORIGINAL (closed) period.
        await act.Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {jan:yyyy-MM-dd}*");

        var entriesAfter = await GetEntriesAsync(host, documentId);
        entriesAfter.Should().BeEquivalentTo(entriesBefore);

        // And no posting log must be created for forbidden operation.
        var repostLogs = await GetPostingLogsAsync(host, logWindow, documentId, PostingOperation.Repost);
        repostLogs.Should().BeEmpty();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task EnsureAsync(string code, string name, AccountType type, StatementSection section)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);

                return;
            }

            await svc.CreateAsync(new CreateAccountRequest(
                Code: code,
                Name: name,
                Type: type,
                StatementSection: section,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: periodUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: amount);
            },
            CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await unposting.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();

        await reposting.RepostAsync(
            documentId,
            postNew: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: periodUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: amount);
            },
            CancellationToken.None);
    }

    private static async Task<IReadOnlyList<NGB.Accounting.Registers.AccountingEntry>> GetEntriesAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        return await reader.GetByDocumentAsync(documentId, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<object>> GetPostingLogsAsync(
        IHost host,
        PostingLogTestWindow window,
        Guid documentId,
        PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            DocumentId = documentId,
            Operation = operation,
            PageSize = 200
        }, CancellationToken.None);

        return page.Records.Cast<object>().ToArray();
    }
}
