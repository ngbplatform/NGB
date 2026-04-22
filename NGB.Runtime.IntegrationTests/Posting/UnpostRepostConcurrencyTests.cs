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

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepostConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnpostAsync_TwoConcurrentCalls_Idempotent_NoDuplicateStorno_NoDuplicatePostingLog()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[]
        {
            RunUnpostAsync(host, documentId, gate),
            RunUnpostAsync(host, documentId, gate)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks)
            .WaitAsync(TimeSpan.FromSeconds(30));

        outcomes.Count(o => o.Succeeded).Should().BeGreaterThanOrEqualTo(1);
        outcomes.Count(o => !o.Succeeded).Should().BeLessThanOrEqualTo(1);

        var failure = outcomes.SingleOrDefault(o => !o.Succeeded)?.Error;
        if (failure is not null)
        {
            failure.Should().BeOfType<NotSupportedException>();
            failure.Message.Should().Contain("already in progress", because: failure.Message);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Entries: original + storno (exactly once)
        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries.Count(e => e.IsStorno).Should().Be(1);

        // Turnovers: original + storno should offset (debit=credit for both accounts)
        var turnovers = await sp.GetRequiredService<IAccountingTurnoverReader>()
            .GetForPeriodAsync(period, CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var income = turnovers.Single(x => x.AccountCode == "90.1");
        income.DebitAmount.Should().Be(100m);
        income.CreditAmount.Should().Be(100m);

        // Posting log: exactly one completed record for Unpost
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1),
            DocumentId = documentId,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        logPage.Records.Should().HaveCount(1);
        logPage.Records.Single().CompletedAtUtc.Should().NotBeNull();
        logPage.Records.Single().Status.Should().Be(PostingStateStatus.Completed);
    }

    [Fact]
    public async Task RepostAsync_TwoConcurrentCalls_Idempotent_NoDuplicateStornoOrNew_NoDuplicatePostingLog()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[]
        {
            RunRepostAsync(host, documentId, periodUtc, newAmount: 200m, gate),
            RunRepostAsync(host, documentId, periodUtc, newAmount: 200m, gate)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks)
            .WaitAsync(TimeSpan.FromSeconds(30));

        outcomes.Count(o => o.Succeeded).Should().BeGreaterThanOrEqualTo(1);
        outcomes.Count(o => !o.Succeeded).Should().BeLessThanOrEqualTo(1);

        var failure = outcomes.SingleOrDefault(o => !o.Succeeded)?.Error;
        if (failure is not null)
        {
            failure.Should().BeOfType<NotSupportedException>();
            failure.Message.Should().Contain("already in progress", because: failure.Message);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Entries: original (100) + storno (100) + new (200) => 3 total
        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(3);
        entries.Count(e => e.IsStorno).Should().Be(1);

        // Turnovers are cumulative register movements (original+storno+new) and must not duplicate.
        var turnovers = await sp.GetRequiredService<IAccountingTurnoverReader>()
            .GetForPeriodAsync(period, CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(300m);
        cash.CreditAmount.Should().Be(100m);

        var income = turnovers.Single(x => x.AccountCode == "90.1");
        income.DebitAmount.Should().Be(100m);
        income.CreditAmount.Should().Be(300m);

        // Posting log: exactly one completed record for Repost
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1),
            DocumentId = documentId,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        logPage.Records.Should().HaveCount(1);
        logPage.Records.Single().CompletedAtUtc.Should().NotBeNull();
        logPage.Records.Single().Status.Should().Be(PostingStateStatus.Completed);
    }

    private static async Task<Outcome> RunUnpostAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();

            await unposting.UnpostAsync(documentId, CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunRepostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        decimal newAmount,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();

            await reposting.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    ctx.Post(documentId, periodUtc, debit, credit, newAmount);
                },
                CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

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

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}
