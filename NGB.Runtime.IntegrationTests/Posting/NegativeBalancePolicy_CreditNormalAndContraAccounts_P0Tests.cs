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
public sealed class NegativeBalancePolicy_CreditNormalAndContraAccounts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime DayUtc = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_WhenCreditNormalAccountWouldGoBelowZero_Forbid_ShouldThrow_AndWriteNothing()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedLiabilityScenarioAsync(host);

        // Build a liability balance first: Cash +100 / Accounts Payable +100
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "50", creditCode: "60", amount: 100m);

        // Pay it down to zero: Accounts Payable -100 / Cash -100 (OK)
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "60", creditCode: "50", amount: 100m);

        // Try to overpay by 1 => Accounts Payable would become negative (debit-balance)
        var documentId = Guid.CreateVersion7();

        // Act
        Func<Task> act = () => PostAsync(host, documentId, DayUtc, debitCode: "60", creditCode: "50", amount: 1m);

        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected:*60*policy=Forbid*period=2026-01-01*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingEntryReader>()
                .GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty("forbidden negative balance must rollback all writes");

        var logPage = await sp.GetRequiredService<IPostingStateReader>()
            .GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-1),
                ToUtc = DateTime.UtcNow.AddDays(1),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

        logPage.Records.Should().BeEmpty("posting_log must rollback on forbidden negative balance");
    }

    [Fact]
    public async Task PostAsync_WhenContraAccountWouldGoBelowZero_Forbid_ShouldThrow_AndWriteNothing()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedContraScenarioAsync(host);

        // Create accumulated depreciation balance: Depreciation expense +100 / Accumulated Depreciation +100
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "91", creditCode: "02", amount: 100m);

        // Reverse exactly to zero (OK): AccumDep -100 / Depreciation expense -100
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "02", creditCode: "91", amount: 100m);

        // Try to reverse below zero by 1 => AccumDep would become negative (debit-balance)
        var documentId = Guid.CreateVersion7();

        // Act
        Func<Task> act = () => PostAsync(host, documentId, DayUtc, debitCode: "02", creditCode: "91", amount: 1m);

        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance projected:*02*policy=Forbid*period=2026-01-01*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingEntryReader>()
                .GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty();

        var logPage = await sp.GetRequiredService<IPostingStateReader>()
            .GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddDays(-1),
                ToUtc = DateTime.UtcNow.AddDays(1),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

        logPage.Records.Should().BeEmpty();
    }

    private static async Task SeedLiabilityScenarioAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash must be Allow so it doesn't participate in negative-balance enforcement in this test.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Credit-normal liability with Forbid.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "60",
            Name: "Accounts Payable",
            Type: AccountType.Liability,
            StatementSection: StatementSection.Liabilities,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid
        ), CancellationToken.None);
    }

    private static async Task SeedContraScenarioAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "01",
            Name: "Equipment",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Contra asset (credit-normal) with Forbid.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "02",
            Name: "Accumulated Depreciation",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            IsContra: true,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid
        ), CancellationToken.None);

        // Expense must be Allow so it doesn't participate in negative-balance enforcement.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Depreciation Expense",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, dateUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
