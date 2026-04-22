using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

[Collection(PostgresCollection.Name)]
public sealed class PostingRecoveryAfterFailureTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_WhenFirstAttemptFailsInsideTransaction_DoesNotPoisonSubsequentPosting()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingPostingValidator>();
                services.AddSingleton<IAccountingPostingValidator>(new FailOnceValidator());
            });

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var failedDocId = Guid.CreateVersion7();
        var okDocId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Act 1: first attempt fails (inside posting transaction).
        Func<Task> failedAct = () => PostOnceAsync(host, failedDocId, period, amount: 100m, CancellationToken.None);
        await failedAct.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated validator failure*");

        // Assert after failure: NOTHING written (including turnovers, because there was no successful posting yet).
        await AssertNoSideEffectsForPeriodAsync(host, failedDocId, period);

        // Act 2: second posting in the same host/process must succeed (no poisoned transaction / pooled connection).
        await PostOnceAsync(host, okDocId, period, amount: 100m, CancellationToken.None);

        // Assert successful doc has effects.
        await AssertPostedAsync(host, okDocId, period);

        // And failed doc still has no document-scoped effects (turnovers are period-aggregated, so don't assert them here).
        await AssertNoDocumentScopedEffectsAsync(host, failedDocId);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            "50",
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            "90.1",
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct2) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct2);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount);
            },
            ct: ct);
    }

    private static async Task AssertNoSideEffectsForPeriodAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None)).Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("posting_log must rollback with the transaction");
    }

    private static async Task AssertNoDocumentScopedEffectsAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }

    private static async Task AssertPostedAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().HaveCount(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None)).Should().NotBeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        page.Records.Should().ContainSingle(r => r.CompletedAtUtc != null);
    }

    private sealed class FailOnceValidator : IAccountingPostingValidator
    {
        private int _calls;

        public void Validate(IReadOnlyList<AccountingEntry> entries)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new NotSupportedException("Simulated validator failure (fail-once).");
        }
    }
}
