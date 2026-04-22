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
public sealed class NegativeBalancePolicyTests(PostgresTestFixture fixture)
{
    [Theory]
    [InlineData(NegativeBalancePolicy.Allow)]
    [InlineData(NegativeBalancePolicy.Warn)]
    public async Task PostAsync_NegativeBalance_AllowOrWarn_AllowsPostingAndCompletesLog(NegativeBalancePolicy cashPolicy)
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host, cashPolicy);

        // Act
        await PostCashOutAsync(host, documentId, period, amount: 100m);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().HaveCount(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        // Cash (credit 100)
        var cash = turnovers.Single(t => t.AccountCode == Cash);
        cash.DebitAmount.Should().Be(0m);
        cash.CreditAmount.Should().Be(100m);

        // Expenses (debit 100)
        var expenses = turnovers.Single(t => t.AccountCode == Expenses);
        expenses.DebitAmount.Should().Be(100m);
        expenses.CreditAmount.Should().Be(0m);

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
        page.Records[0].CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task PostAsync_NegativeBalance_Forbid_RollsBackAndThrowsClearError()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host, NegativeBalancePolicy.Forbid);

        // Act
        Func<Task> act = async () => await PostCashOutAsync(host, documentId, period, amount: 100m);

        // Assert
        var ex = await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>();

        // We want to ensure the failure is the NegativeBalancePolicy guard (not some incidental error).
        ex.Which.Message.Should().Contain("Negative balance projected:");
        ex.Which.Message.Should().Contain(Cash);
        ex.Which.Message.Should().Contain("policy=Forbid");
        ex.Which.Message.Should().Contain("period=2026-01-01");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            // Full rollback guarantees: nothing written.
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);
            turnovers.Should().BeEmpty();

            // Posting log is written in the same transaction: it must not remain.
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

    private const string Cash = "50";
    private const string Expenses = "91";

    private static async Task SeedMinimalCoaAsync(IHost host, NegativeBalancePolicy cashPolicy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash (Asset, credit will decrease balance; from 0 it becomes negative)
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: cashPolicy
        ), CancellationToken.None);

        // Expenses (Expense is usually debit-normal, so Debit increases balance; always allow)
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Expenses,
            Name: "Expenses",
            AccountType.Expense,
            StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostCashOutAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                // Cash OUT: Debit Expenses / Credit Cash
                var debit = chart.Get(Expenses);
                var credit = chart.Get(Cash);

                ctx.Post(
                    documentId,
                    period,
                    debit,
                    credit,
                    amount: amount
                );
            },
            ct: CancellationToken.None
        );
    }
}
