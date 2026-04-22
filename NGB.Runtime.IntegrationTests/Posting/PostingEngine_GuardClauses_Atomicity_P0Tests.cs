using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_GuardClauses_Atomicity_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WithNullPostingAction_Throws_AndWritesNothing()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            Func<Task> act = () => posting.PostAsync(
                PostingOperation.Post,
                postingAction: null!,
                manageTransaction: true,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
            ex.Which.ParamName.Should().Be("postingAction");
        }

        await AssertNoWritesAsync(host, documentId: Guid.CreateVersion7(), period: new DateOnly(2026, 1, 1), logWindow);
    }

    [Fact]
    public async Task PostAsync_WhenPostingActionThrows_AfterPostingEntries_DoesNotStartTransaction_AndWritesNothing()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        var docId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                ctx.Post(docId, period, chart.Get("50"), chart.Get("90.1"), 1m);

                throw new NotSupportedException("boom");
            }, manageTransaction: true, ct: CancellationToken.None);
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Be("boom");

        await AssertNoWritesAsync(host, docId, AccountingPeriod.FromDateTime(period), logWindow);
    }

    [Fact]
    public async Task PostAsync_WithEmptyDocumentId_ThrowsAfterLock_AndLeavesNoPartialWrites()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        var docId = Guid.Empty;
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(docId, period, chart.Get("50"), chart.Get("90.1"), 1m);
            }, manageTransaction: true, ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("documentId");

        await AssertNoWritesAsync(host, docId, AccountingPeriod.FromDateTime(period), logWindow);
    }

    [Fact]
    public async Task PostAsync_WhenValidatorThrowsAfterTryBegin_RollsBack_AndDoesNotPersistPostingLog()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                // First entry defines documentId for posting_log. Second violates the invariant.
                ctx.Post(doc1, period, chart.Get("50"), chart.Get("90.1"), 1m);
                ctx.Post(doc2, period, chart.Get("50"), chart.Get("90.1"), 1m);
            }, manageTransaction: true, ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ErrorCode.Should().Be(NgbArgumentInvalidException.Code);
        ex.Which.ParamName.Should().Be("entries");
        ex.Which.Reason.Should().Contain("same DocumentId");

        await AssertNoWritesAsync(host, doc1, AccountingPeriod.FromDateTime(period), logWindow);
        await AssertNoWritesAsync(host, doc2, AccountingPeriod.FromDateTime(period), logWindow);
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
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task AssertNoWritesAsync(IHost host, Guid documentId, DateOnly period, PostingLogTestWindow logWindow)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var entries = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var turnovers = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        (await entries.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();
        (await turnovers.GetForPeriodAsync(period, CancellationToken.None)).Should().BeEmpty();

        var page = await log.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = documentId,
            PageSize = 10_000,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }
}
