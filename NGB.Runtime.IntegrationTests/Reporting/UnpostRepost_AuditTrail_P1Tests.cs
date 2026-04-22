using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepost_AuditTrail_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task Unpost_WritesStornoLines_AndPostingLogHasPostAndUnpost()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        await PostAsync(host, doc, amount: 100m);

        // Act
        await UnpostAsync(host, doc);

        // Assert: General Journal contains original + storno for this document
        var lines = await ReadAllGeneralJournalLinesForDocumentAsync(host, doc);

        lines.Should().HaveCount(2);
        lines.Count(l => l.IsStorno).Should().Be(1);

        // Assert: Posting log has Post + Unpost
        var log = await ReadPostingLogAsync(host, doc);

        log.Should().ContainSingle(x => x.Operation == PostingOperation.Post);
        log.Should().ContainSingle(x => x.Operation == PostingOperation.Unpost);
    }

    [Fact]
    public async Task Repost_WritesStornoOfOld_ThenNewLines_AndPostingLogHasPostAndRepost()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        await PostAsync(host, doc, amount: 100m);

        // Act: repost with a new amount
        await RepostAsync(host, doc, newAmount: 250m);

        // Assert: General Journal contains old + storno + new (3 lines) OR (old+storno+new)=3
        // In this engine: repost stornoes existing entries and writes new entries; old entries remain.
        var lines = await ReadAllGeneralJournalLinesForDocumentAsync(host, doc);

        lines.Should().HaveCount(3);
        lines.Count(l => l.IsStorno).Should().Be(1);

        lines.Single(l => !l.IsStorno && l.Amount == 250m).Should().NotBeNull();

        // Posting log has Post + Repost
        var log = await ReadPostingLogAsync(host, doc);
        log.Should().ContainSingle(x => x.Operation == PostingOperation.Post);
        log.Should().ContainSingle(x => x.Operation == PostingOperation.Repost);
    }

    private static async Task SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task GetOrCreateAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                return;
            }

            _ = await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);
    }

    private static async Task PostAsync(IHost host, Guid documentId, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                ctx.Post(
                    documentId,
                    PeriodUtc,
                    chart.Get("50"),
                    chart.Get("90.1"),
                    amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await svc.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostAsync(IHost host, Guid documentId, decimal newAmount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<RepostingService>();

        await svc.RepostAsync(
            documentId: documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                ctx.Post(
                    documentId,
                    PeriodUtc,
                    chart.Get("50"),
                    chart.Get("90.1"),
                    newAmount);
            },
            ct: CancellationToken.None);
    }

    private static async Task<IReadOnlyList<GeneralJournalLine>> ReadAllGeneralJournalLinesForDocumentAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reader = sp.GetRequiredService<IGeneralJournalReader>();

        var all = new List<GeneralJournalLine>();
        GeneralJournalCursor? cursor = null;

        while (true)
        {
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100,
                Cursor = cursor
            }, CancellationToken.None);

            all.AddRange(page.Lines.Where(l => l.DocumentId == documentId));

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        return all;
    }

        private static async Task<IReadOnlyList<PostingStateRecord>> ReadPostingLogAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reader = sp.GetRequiredService<IPostingStateReader>();

        var fromUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            PageSize = 50
        }, CancellationToken.None);

        return page.Records;
    }
}
