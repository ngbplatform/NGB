using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
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
public sealed class PostingEngine_ExternalTransactionModeTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws_AndDoesNotWrite()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        // Act
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var posting = sp.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);
        };

        // Assert
        await act.Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*manageTransaction=false requires an active transaction*");

        await AssertNoSideEffectsAsync(host, documentId, AccountingPeriod.FromDateTime(period));
    }

    [Fact]
    public async Task PostAsync_ManageTransactionFalse_ExternalCommit_PersistsWrites_AndDoesNotAutoCommit()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            // PostingEngine must NOT commit in external transaction mode.
            uow.HasActiveTransaction.Should().BeTrue();

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);
            entries[0].Amount.Should().Be(100m);
            entries[0].Debit.Code.Should().Be("50");
            entries[0].Credit.Code.Should().Be("90.1");

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(AccountingPeriod.FromDateTime(period), CancellationToken.None);
            turnovers.Should().ContainSingle(x => x.AccountCode == "50" && x.DebitAmount == 100m && x.CreditAmount == 0m);
            turnovers.Should().ContainSingle(x => x.AccountCode == "90.1" && x.DebitAmount == 0m && x.CreditAmount == 100m);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-1),
                ToUtc = DateTime.UtcNow.AddDays(1),
                DocumentId = documentId,
                Operation = PostingOperation.Post
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        }
    }

    [Fact]
    public async Task PostAsync_ManageTransactionFalse_ExternalRollback_RollsBackWrites_AndLog()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            // PostingEngine must NOT rollback in external transaction mode.
            uow.HasActiveTransaction.Should().BeTrue();

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert
        await AssertNoSideEffectsAsync(host, documentId, AccountingPeriod.FromDateTime(period));
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        // Revenue
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
        turnovers.Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }
}
