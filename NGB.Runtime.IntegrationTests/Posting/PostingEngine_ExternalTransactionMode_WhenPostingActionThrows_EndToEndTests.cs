using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P1 coverage: in external transaction mode (manageTransaction=false), PostingEngine must not auto-commit
/// nor auto-rollback. If postingAction throws, the transaction must remain active and the caller decides.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_ExternalTransactionMode_WhenPostingActionThrows_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_ManageTransactionFalse_WhenPostingActionThrows_LeavesTransactionActive_AndRollbackCleansAll()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            // Act: postingAction throws BEFORE any ctx.Post call.
            Func<Task> act = async () =>
                await posting.PostAsync(
                    operation: PostingOperation.Post,
                    postingAction: (_, _) => throw new NotSupportedException("Simulated postingAction failure"),
                    manageTransaction: false,
                    ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("Simulated postingAction failure");

            // Engine must not commit/rollback for external transaction mode.
            uow.HasActiveTransaction.Should().BeTrue();

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert: no audit garbage, no entries, no turnovers.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            (await turnoverReader.GetForPeriodAsync(new DateOnly(2026, 1, 1), CancellationToken.None)).Should().BeEmpty();

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
}
