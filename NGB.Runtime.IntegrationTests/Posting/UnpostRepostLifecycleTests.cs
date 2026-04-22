using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepostLifecycleTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnpostAsync_PostedDocument_WritesStornoAndNetEffectIsZero()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        await PostCashRevenueAsync(host, documentId, period, amount: 100m);

        // Act
        await UnpostAsync(host, documentId);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries.Count(x => x.IsStorno).Should().Be(1, "Unpost must write exactly one storno entry for the original posting");

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);
    }

    [Fact]
    public async Task UnpostAsync_NoEntries_DoesNothing()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();

        // Act
        await UnpostAsync(host, documentId);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().BeEmpty("UnpostAsync should be a no-op when the document has no posted entries");
    }

    [Fact]
    public async Task RepostAsync_PostedDocument_WritesStornoAndNewEntries_AndNetEffectEqualsNewPosting()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        await PostCashRevenueAsync(host, documentId, period, amount: 100m);

        // Act
        await RepostCashRevenueAsync(host, documentId, period, newAmount: 200m);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(3);
        entries.Count(x => x.IsStorno).Should().Be(1, "Repost must storno old entries exactly once and then post new entries");

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(300m, "original 100 + new 200 are debit turnovers");
        cash.CreditAmount.Should().Be(100m, "storno of the original 100 is credit turnover");

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m, "storno of the original 100 is debit turnover");
        revenue.CreditAmount.Should().Be(300m, "original 100 + new 200 are credit turnovers");
    }

    [Fact]
    public async Task RepostAsync_NoEntries_Throws()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        Func<Task> act = () => RepostCashRevenueAsync(host, documentId, period, newAmount: 200m);

        // Assert
        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*No entries to repost*");
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Act
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

        // Assert
        // no-op
    }

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        // Act
        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(
                    documentId: documentId,
                    period: period,
                    debit: debit,
                    credit: credit,
                    amount: amount
                );
            },
            ct: CancellationToken.None
        );

        // Assert
        // no-op
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var unposting = sp.GetRequiredService<UnpostingService>();

        // Act
        await unposting.UnpostAsync(documentId, CancellationToken.None);

        // Assert
        // no-op
    }

    private static async Task RepostCashRevenueAsync(IHost host, Guid documentId, DateTime period, decimal newAmount)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reposting = sp.GetRequiredService<RepostingService>();

        // Act
        await reposting.RepostAsync(
            documentId: documentId,
            postNew: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync();

                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(
                    documentId: documentId,
                    period: period,
                    debit: debit,
                    credit: credit,
                    amount: newAmount
                );
            },
            ct: CancellationToken.None
        );

        // Assert
        // no-op
    }
}
