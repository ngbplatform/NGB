using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_ExternalTransactionMode_UnpostRepostTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws_AndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var posting = sp.GetRequiredService<PostingEngine>();

            Func<Task> act = async () =>
            {
                await posting.PostAsync(
                    operation: PostingOperation.Unpost,
                    // Important: PostingEngine validates that at least one entry is produced BEFORE
                    // checking manageTransaction=false requires an external transaction.
                    // So we must generate a real entry here to reach the transaction-guard.
                    postingAction: async (ctx, ct) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(ct);
                        var debit = chart.Get("50");
                        var credit = chart.Get("90.1");

                        // The entry content is irrelevant for this test; we only need a non-empty batch.
                        ctx.Post(documentId, period, debit, credit, amount: 1m, isStorno: true);
                    },
                    manageTransaction: false,
                    ct: CancellationToken.None);
            };

            await act.Should()
                .ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*manageTransaction=false requires an active transaction*");
        }

        await AssertDocumentEntriesCountAsync(host, documentId, expected: 1);
        await AssertPostingLogDoesNotContainAsync(host, documentId, PostingOperation.Unpost);
    }

    [Fact]
    public async Task UnpostAsync_ManageTransactionFalse_ExternalCommit_PersistsStorno_AndDoesNotAutoCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var original = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            original.Should().HaveCount(1);

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            await posting.PostAsync(
                operation: PostingOperation.Unpost,
                postingAction: async (ctx, ct) =>
                {
                    foreach (var s in AccountingStornoFactory.Create(original))
                    {
                        ctx.Post(
                            documentId: s.DocumentId,
                            period: s.Period,
                            debit: s.Debit,
                            credit: s.Credit,
                            amount: s.Amount,
                            debitDimensions: s.DebitDimensions,
                            creditDimensions: s.CreditDimensions,
                            isStorno: s.IsStorno);

                        var created = ctx.Entries[^1];
                        created.DebitDimensionSetId = s.DebitDimensionSetId;
                        created.CreditDimensionSetId = s.CreditDimensionSetId;
                    }

                    await Task.CompletedTask;
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            // PostingEngine must NOT auto-commit in external transaction mode.
            uow.HasActiveTransaction.Should().BeTrue();

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(2);

            entries.Should().ContainSingle(x =>
                !x.IsStorno && x.Amount == 100m && x.Debit.Code == "50" && x.Credit.Code == "90.1");
            entries.Should().ContainSingle(x =>
                x.IsStorno && x.Amount == 100m && x.Debit.Code == "90.1" && x.Credit.Code == "50");

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(AccountingPeriod.FromDateTime(period), CancellationToken.None);

            turnovers.Should().ContainSingle(x => x.AccountCode == "50" && x.DebitAmount == 100m && x.CreditAmount == 100m);
            turnovers.Should().ContainSingle(x => x.AccountCode == "90.1" && x.DebitAmount == 100m && x.CreditAmount == 100m);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-7),
                ToUtc = DateTime.UtcNow.AddDays(7),
                DocumentId = documentId,
                Operation = PostingOperation.Unpost,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().ContainSingle();
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        }
    }

    [Fact]
    public async Task UnpostAsync_ManageTransactionFalse_ExternalRollback_RollsBackStorno_AndLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var original = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            original.Should().HaveCount(1);

            await uow.BeginTransactionAsync(CancellationToken.None);

            await posting.PostAsync(
                operation: PostingOperation.Unpost,
                postingAction: async (ctx, ct) =>
                {
                    foreach (var s in AccountingStornoFactory.Create(original))
                    {
                        ctx.Post(
                            documentId: s.DocumentId,
                            period: s.Period,
                            debit: s.Debit,
                            credit: s.Credit,
                            amount: s.Amount,
                            debitDimensions: s.DebitDimensions,
                            creditDimensions: s.CreditDimensions,
                            isStorno: s.IsStorno);

                        var created = ctx.Entries[^1];
                        created.DebitDimensionSetId = s.DebitDimensionSetId;
                        created.CreditDimensionSetId = s.CreditDimensionSetId;
                    }

                    await Task.CompletedTask;
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await AssertDocumentEntriesCountAsync(host, documentId, expected: 1);
        await AssertPostingLogDoesNotContainAsync(host, documentId, PostingOperation.Unpost);
    }

    [Fact]
    public async Task RepostAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws_AndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var posting = sp.GetRequiredService<PostingEngine>();

            Func<Task> act = async () =>
            {
                await posting.PostAsync(
                    operation: PostingOperation.Repost,
                    // See comment in Unpost-test: we must produce entries to hit the transaction guard.
                    postingAction: async (ctx, ct) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(ct);
                        var debit = chart.Get("50");
                        var credit = chart.Get("90.1");

                        // Simulate a repost batch (storno + new). The exact amounts do not matter here.
                        ctx.Post(documentId, period, debit, credit, amount: 1m, isStorno: true);
                        ctx.Post(documentId, period, debit, credit, amount: 2m, isStorno: false);
                    },
                    manageTransaction: false,
                    ct: CancellationToken.None);
            };

            await act.Should()
                .ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*manageTransaction=false requires an active transaction*");
        }

        await AssertDocumentEntriesCountAsync(host, documentId, expected: 1);
        await AssertPostingLogDoesNotContainAsync(host, documentId, PostingOperation.Repost);
    }

    [Fact]
    public async Task RepostAsync_ManageTransactionFalse_ExternalCommit_PersistsStornoAndNewEntries_AndDoesNotAutoCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var original = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            original.Should().HaveCount(1);

            await uow.BeginTransactionAsync(CancellationToken.None);

            await posting.PostAsync(
                operation: PostingOperation.Repost,
                postingAction: async (ctx, ct) =>
                {
                    // Storno original
                    foreach (var s in AccountingStornoFactory.Create(original))
                    {
                        ctx.Post(
                            documentId: s.DocumentId,
                            period: s.Period,
                            debit: s.Debit,
                            credit: s.Credit,
                            amount: s.Amount,
                            debitDimensions: s.DebitDimensions,
                            creditDimensions: s.CreditDimensions,
                            isStorno: s.IsStorno);

                        var created = ctx.Entries[^1];
                        created.DebitDimensionSetId = s.DebitDimensionSetId;
                        created.CreditDimensionSetId = s.CreditDimensionSetId;
                    }

                    // New posting (changed amount)
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    ctx.Post(documentId, period, debit, credit, amount: 200m);

                    await Task.CompletedTask;
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            uow.HasActiveTransaction.Should().BeTrue();
            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert net effect is 200 (old 100 + storno -100 + new 200)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(3);

            entries.Should().ContainSingle(x =>
                !x.IsStorno && x.Amount == 100m && x.Debit.Code == "50" && x.Credit.Code == "90.1");
            entries.Should().ContainSingle(x =>
                x.IsStorno && x.Amount == 100m && x.Debit.Code == "90.1" && x.Credit.Code == "50");
            entries.Should().ContainSingle(x =>
                !x.IsStorno && x.Amount == 200m && x.Debit.Code == "50" && x.Credit.Code == "90.1");

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(AccountingPeriod.FromDateTime(period), CancellationToken.None);

            turnovers.Should().ContainSingle(x => x.AccountCode == "50" && x.DebitAmount == 300m && x.CreditAmount == 100m);
            turnovers.Should().ContainSingle(x => x.AccountCode == "90.1" && x.DebitAmount == 100m && x.CreditAmount == 300m);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-7),
                ToUtc = DateTime.UtcNow.AddDays(7),
                DocumentId = documentId,
                Operation = PostingOperation.Repost,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().ContainSingle();
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        }
    }

    [Fact]
    public async Task RepostAsync_ManageTransactionFalse_ExternalRollback_RollsBackStornoAndNewEntries_AndLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        await PostSingleEntryAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var original = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            original.Should().HaveCount(1);

            await uow.BeginTransactionAsync(CancellationToken.None);

            await posting.PostAsync(
                operation: PostingOperation.Repost,
                postingAction: async (ctx, ct) =>
                {
                    foreach (var s in AccountingStornoFactory.Create(original))
                    {
                        ctx.Post(
                            documentId: s.DocumentId,
                            period: s.Period,
                            debit: s.Debit,
                            credit: s.Credit,
                            amount: s.Amount,
                            debitDimensions: s.DebitDimensions,
                            creditDimensions: s.CreditDimensions,
                            isStorno: s.IsStorno);

                        var created = ctx.Entries[^1];
                        created.DebitDimensionSetId = s.DebitDimensionSetId;
                        created.CreditDimensionSetId = s.CreditDimensionSetId;
                    }

                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");
                    ctx.Post(documentId, period, debit, credit, amount: 200m);

                    await Task.CompletedTask;
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await AssertDocumentEntriesCountAsync(host, documentId, expected: 1);
        await AssertPostingLogDoesNotContainAsync(host, documentId, PostingOperation.Repost);
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

    private static async Task PostSingleEntryAsync(IHost host, Guid documentId, DateTime period, decimal amount)
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

                ctx.Post(documentId, period, debit, credit, amount);

                await Task.CompletedTask;
            },
            ct: CancellationToken.None);
    }

    private static async Task AssertDocumentEntriesCountAsync(IHost host, Guid documentId, int expected)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().HaveCount(expected);
    }

    private static async Task AssertPostingLogDoesNotContainAsync(IHost host, Guid documentId, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = operation,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }
}
