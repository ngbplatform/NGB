using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepostIdempotencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnpostAsync_SameDocumentTwice_DoesNotDuplicateStornoEntries()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act
        await UnpostOnceAsync(host, documentId);
        await UnpostOnceAsync(host, documentId); // idempotent repeat

        // Assert
        var entries = await ReadEntriesAsync(host, documentId);
        entries.Should().HaveCount(2);

        entries.Count(e => e.IsStorno).Should().Be(1);

        var turnovers = await ReadTurnoversAsync(host, period);
        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);
    }

    [Fact]
    public async Task RepostAsync_SameDocumentTwice_DoesNotDuplicateStornoOrNewEntries()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act
        await RepostOnceAsync(host, documentId, period, amount: 200m);
        await RepostOnceAsync(host, documentId, period, amount: 200m); // idempotent repeat

        // Assert
        var entries = await ReadEntriesAsync(host, documentId);
        entries.Should().HaveCount(3);

        entries.Count(e => e.IsStorno).Should().Be(1);

        // Original (100) + storno (100) + new (200)
        var turnovers = await ReadTurnoversAsync(host, period);
        var cash = turnovers.Single(x => x.AccountCode == "50");
        cash.DebitAmount.Should().Be(300m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == "90.1");
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(300m);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Revenue
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
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
                    documentId,
                    period,
                    debit,
                    credit,
                    amount: amount
                );
            },
            ct: CancellationToken.None
        );

        // Assert
        // no-op
    }

    private static async Task UnpostOnceAsync(IHost host, Guid documentId)
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

    private static async Task RepostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reposting = sp.GetRequiredService<RepostingService>();

        // Act
        await reposting.RepostAsync(
            documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

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

        // Assert
        // no-op
    }

    private static async Task<IReadOnlyList<AccountingEntry>> ReadEntriesAsync(IHost host, Guid documentId)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

        // Act
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        // Assert
        return entries;
    }

    private static async Task<IReadOnlyList<AccountingTurnover>> ReadTurnoversAsync(IHost host, DateTime period)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();

        // Act
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        // Assert
        return turnovers;
    }
}
