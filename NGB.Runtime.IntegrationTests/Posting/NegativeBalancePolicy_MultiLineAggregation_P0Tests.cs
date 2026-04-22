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

/// <summary>
/// P0: NegativeBalancePolicy operational enforcement must aggregate a posting's deltas per
/// (account + dimension dimensions) BEFORE evaluating projected balances.
///
/// Without this, a single document containing multiple lines can bypass Forbid policies.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NegativeBalancePolicy_MultiLineAggregation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";
    private const string Expense = "90.2";

    [Fact]
    public async Task PostAsync_MultipleCreditsSameCashKey_AggregatesAndRejects_WhenForbid()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedCoaAsync(host, cashPolicy: NegativeBalancePolicy.Forbid, cashDimensionRequired: false);

        var dayUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Two lines in the same document: projected cash should be -120, not evaluated per-line.
                    ctx.Post(documentId, dayUtc, chart.Get(Expense), chart.Get(Cash), 60m);
                    ctx.Post(documentId, dayUtc, chart.Get(Expense), chart.Get(Cash), 60m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected*policy=Forbid*");

        await AssertNoWritesAsync(host, documentId);
    }

    [Fact]
    public async Task PostAsync_DebitAndCreditSameCashKey_NetsToZero_Allows_WhenForbid()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedCoaAsync(host, cashPolicy: NegativeBalancePolicy.Forbid, cashDimensionRequired: false);

        var dayUtc = new DateTime(2026, 1, 13, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Net 0 delta for the same Cash key (no dimensions).
                    ctx.Post(documentId, dayUtc, chart.Get(Cash), chart.Get(Revenue), 100m);
                    ctx.Post(documentId, dayUtc, chart.Get(Expense), chart.Get(Cash), 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().HaveCount(2);

            var logReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-1),
                ToUtc = DateTime.UtcNow.AddDays(1),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        }
    }

    private static async Task SeedCoaAsync(
        IHost host,
        NegativeBalancePolicy cashPolicy,
        bool cashDimensionRequired)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            DimensionRules: cashDimensionRequired
                ? new[] { new AccountDimensionRuleRequest("building", true, Ordinal: 10) }
                : null,
            NegativeBalancePolicy: cashPolicy
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Expense,
            Name: "Expense",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task AssertNoWritesAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty("failed posting must rollback register writes");

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("posting_log must rollback on forbidden negative balance");
    }
}
