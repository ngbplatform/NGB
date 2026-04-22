using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounting;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class NegativeBalancePolicy_EndToEndTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Expense = "90.2";

    [Theory]
    [InlineData(NegativeBalancePolicy.Allow)]
    [InlineData(NegativeBalancePolicy.Warn)]
    public async Task PostAsync_CreatesNegativeBalance_WhenPolicyAllowsOrWarns_Succeeds(NegativeBalancePolicy policy)
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedCoaForNegativeBalanceAsync(host, cashPolicy: policy);

        var period = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        // Act
        await using (var scopePosting = host.Services.CreateAsyncScope())
        {
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                // Credit Cash without prior balance => negative cash.
                ctx.Post(documentId, period, chart.Get(Expense), chart.Get(Cash), 100m);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
        }


        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().HaveCount(1);

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        logPage.Records.Should().HaveCount(1);
        logPage.Records[0].Status.Should().Be(PostingStateStatus.Completed);
    }

    [Fact]
    public async Task PostAsync_CreatesNegativeBalance_WhenPolicyForbids_Throws_AndWritesNothing()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedCoaForNegativeBalanceAsync(host, cashPolicy: NegativeBalancePolicy.Forbid);

        var period = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        // Act
        Func<Task> act = async () =>
        {
            await using var scopePosting = host.Services.CreateAsyncScope();
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Expense), chart.Get(Cash), 100m);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
        };


        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected*policy=Forbid*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty("posting_log must rollback on forbidden negative balance policy");
    }

    private static async Task SeedCoaForNegativeBalanceAsync(IHost host, NegativeBalancePolicy cashPolicy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: cashPolicy
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Expense,
            "Expense",
            AccountType.Expense,
            StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
