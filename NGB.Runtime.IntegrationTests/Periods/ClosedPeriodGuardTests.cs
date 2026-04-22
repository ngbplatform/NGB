using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodGuardTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_ClosedPeriod_Throws_AndDoesNotWriteEntriesOrTurnovers()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var closedPeriod = DateOnly.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        await CloseMonthAsync(host, closedPeriod);

        var turnoversBefore = await GetTurnoversAsync(host, closedPeriod);
        var balancesBefore = await GetBalancesAsync(host, closedPeriod);

        // Act
        var act = () => PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert
        await act
            .Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {closedPeriod:yyyy-MM-dd}*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);
            turnovers.Should().BeEmpty();

            var balancesReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var balances = await balancesReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);
            balances.Should().BeEquivalentTo(balancesBefore);

            var postingLogReader = sp.GetRequiredService<IPostingStateReader>();
            var postingLog = await GetPostingLogAsync(
                postingLogReader,
                fromUtc: period.AddDays(-1),
                toUtc: period.AddDays(1),
                documentId: documentId,
                operation: PostingOperation.Post,
                ct: CancellationToken.None);
            postingLog.Should().BeEmpty("closed period guard must fail before creating posting_log record");
        }

        // (Optional) ensure no side-effects in turnovers as well.
        var turnoversAfter = await GetTurnoversAsync(host, closedPeriod);
        turnoversAfter.Should().BeEquivalentTo(turnoversBefore);
    }

    [Fact]
    public async Task UnpostAsync_ClosedPeriod_Throws_AndDoesNotWriteStorno()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var closedPeriod = DateOnly.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Post BEFORE closing.
        await PostOnceAsync(host, documentId, period, amount: 100m);

        await CloseMonthAsync(host, closedPeriod);

        var turnoversBefore = await GetTurnoversAsync(host, closedPeriod);
        var balancesBefore = await GetBalancesAsync(host, closedPeriod);

        // Act
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(documentId, CancellationToken.None);
        };

        // Assert
        await act
            .Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {closedPeriod:yyyy-MM-dd}*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);

            var cash = turnovers.Single(x => x.AccountCode == "50");
            cash.DebitAmount.Should().Be(100m);
            cash.CreditAmount.Should().Be(0m);

            var revenue = turnovers.Single(x => x.AccountCode == "90.1");
            revenue.DebitAmount.Should().Be(0m);
            revenue.CreditAmount.Should().Be(100m);

            var balancesReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var balances = await balancesReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);
            balances.Should().BeEquivalentTo(balancesBefore);

            var postingLogReader = sp.GetRequiredService<IPostingStateReader>();
            var unpostLog = await GetPostingLogAsync(
                postingLogReader,
                fromUtc: period.AddDays(-1),
                toUtc: period.AddDays(1),
                documentId: documentId,
                operation: PostingOperation.Unpost,
                ct: CancellationToken.None);
            unpostLog.Should().BeEmpty("closed period guard must prevent writing Unpost posting_log record");
        }

        (await GetTurnoversAsync(host, closedPeriod)).Should().BeEquivalentTo(turnoversBefore);
    }

    [Fact]
    public async Task RepostAsync_ClosedPeriod_Throws_AndDoesNotWriteStornoOrNewEntries()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var closedPeriod = DateOnly.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Post BEFORE closing.
        await PostOnceAsync(host, documentId, period, amount: 100m);

        await CloseMonthAsync(host, closedPeriod);

        var turnoversBefore = await GetTurnoversAsync(host, closedPeriod);
        var balancesBefore = await GetBalancesAsync(host, closedPeriod);

        // Act
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();
            
            await reposting.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    ctx.Post(
                        documentId: documentId,
                        period: period,
                        debit: debit,
                        credit: credit,
                        amount: 200m);
                },
                CancellationToken.None);
        };

        // Assert
        await act
            .Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {closedPeriod:yyyy-MM-dd}*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);

            var cash = turnovers.Single(x => x.AccountCode == "50");
            cash.DebitAmount.Should().Be(100m);
            cash.CreditAmount.Should().Be(0m);

            var revenue = turnovers.Single(x => x.AccountCode == "90.1");
            revenue.DebitAmount.Should().Be(0m);
            revenue.CreditAmount.Should().Be(100m);

            var balancesReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var balances = await balancesReader.GetForPeriodAsync(closedPeriod, CancellationToken.None);
            balances.Should().BeEquivalentTo(balancesBefore);

            var postingLogReader = sp.GetRequiredService<IPostingStateReader>();
            var repostLog = await GetPostingLogAsync(
                postingLogReader,
                fromUtc: period.AddDays(-1),
                toUtc: period.AddDays(1),
                documentId: documentId,
                operation: PostingOperation.Repost,
                ct: CancellationToken.None);
            repostLog.Should().BeEmpty("closed period guard must prevent writing Repost posting_log record");
        }

        (await GetTurnoversAsync(host, closedPeriod)).Should().BeEquivalentTo(turnoversBefore);
    }

    private static async Task<IReadOnlyList<AccountingTurnover>> GetTurnoversAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
        return await reader.GetForPeriodAsync(period, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<AccountingBalance>> GetBalancesAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();
        return await reader.GetForPeriodAsync(period, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<PostingStateRecord>> GetPostingLogAsync(
        IPostingStateReader reader,
        DateTime fromUtc,
        DateTime toUtc,
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct)
    {
        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            Operation = operation,
            PageSize = 100
        }, ct);

        return page.Records;
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Act
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        // Act
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);

        // Assert
        // no-op (guard behavior is verified by subsequent actions)
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        // Act
        await posting.PostAsync(
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
                    amount: amount);
            },
            ct: CancellationToken.None);

        // Assert
        // no-op
    }
}
