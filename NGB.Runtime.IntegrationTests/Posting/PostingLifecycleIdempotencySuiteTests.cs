using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLifecycleIdempotencySuiteTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostUnpostUnpost_SameDocument_IsIdempotent_AndPostingLogIsStable()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        await PostOnceAsync(host, documentId, period, amount: 100m);

        await UnpostOnceAsync(host, documentId);
        await UnpostOnceAsync(host, documentId); // idempotent repeat

        var entries = await ReadEntriesAsync(host, documentId);
        entries.Should().HaveCount(2);
        entries.Count(e => e.IsStorno).Should().Be(1);

        var turnovers = await ReadTurnoversAsync(host, period);
        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);

        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Post)).Should().HaveCount(1);
        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Unpost)).Should().HaveCount(1);
    }

    [Fact]
    public async Task PostRepostRepost_SameDocument_IsIdempotent_AndPostingLogIsStable()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        await RepostOnceAsync(host, documentId, period, amount: 200m);
        await RepostOnceAsync(host, documentId, period, amount: 200m); // idempotent repeat

        var entries = await ReadEntriesAsync(host, documentId);
        entries.Should().HaveCount(3);
        entries.Count(e => e.IsStorno).Should().Be(1);

        var turnovers = await ReadTurnoversAsync(host, period);
        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(300m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(300m);

        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Post)).Should().HaveCount(1);
        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Repost)).Should().HaveCount(1);
    }

    [Fact]
    public async Task PostUnpostRepostThenRepost_SameDocument_IsIdempotent_AndPostingLogIsStable()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);
        await UnpostOnceAsync(host, documentId);

        // NOTE: current RepostingService takes *all* existing entries for the document (including storno)
        // and creates storno for the entire set.
        await RepostOnceAsync(host, documentId, period, amount: 200m);
        await RepostOnceAsync(host, documentId, period, amount: 200m); // idempotent repeat

        var entries = await ReadEntriesAsync(host, documentId);
        entries.Should().HaveCount(5);
        entries.Count(e => e.IsStorno).Should().Be(3, "original unpost adds 1 storno; repost adds storno for both existing entries");

        var turnovers = await ReadTurnoversAsync(host, period);
        var cash = turnovers.Single(x => x.AccountCode == "50");
        (cash.DebitAmount - cash.CreditAmount).Should().Be(200m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        (revenue.CreditAmount - revenue.DebitAmount).Should().Be(200m);

        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Post)).Should().HaveCount(1);
        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Unpost)).Should().HaveCount(1);
        (await ReadPostingLogsAsync(host, documentId, PostingOperation.Repost)).Should().HaveCount(1);
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

    private static async Task<IReadOnlyList<AccountingEntry>> ReadEntriesAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        return await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<AccountingTurnover>> ReadTurnoversAsync(IHost host, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        return await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);
    }

    private static async Task<IReadOnlyList<PostingStateRecord>> ReadPostingLogsAsync(
        IHost host,
        Guid documentId,
        PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reader = sp.GetRequiredService<IPostingStateReader>();

        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            DocumentId = documentId,
            Operation = operation,
            Status = PostingStateStatus.Completed,
            PageSize = 100
        }, CancellationToken.None);

        return page.Records;
    }
}
