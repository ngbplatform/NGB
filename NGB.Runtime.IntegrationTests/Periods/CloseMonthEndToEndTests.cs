using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounting;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonthEndToEndTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonthAsync_HappyPath_WritesBalances_AndMarksPeriodClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Allow);

        var documentId = Guid.CreateVersion7();
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        // Act
        await CloseMonthAsync(host, period);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
            turnovers.Should().HaveCount(2);

            turnovers.Should().ContainSingle(t => t.AccountCode == "50" && t.DebitAmount == 100m && t.CreditAmount == 0m);
            turnovers.Should().ContainSingle(t => t.AccountCode == "90.1" && t.DebitAmount == 0m && t.CreditAmount == 100m);

            var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
            balances.Should().HaveCount(2);

            balances.Should().ContainSingle(b => b.AccountCode == "50" && b.OpeningBalance == 0m && b.ClosingBalance == 100m);
            balances.Should().ContainSingle(b => b.AccountCode == "90.1" && b.OpeningBalance == 0m && b.ClosingBalance == -100m);

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().ContainSingle(p => p.Period == period && p.ClosedBy == "test");
            closed.Single().ClosedAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        }
    }

    [Fact]
    public async Task CloseMonthAsync_AlreadyClosed_Throws_AndDoesNotDuplicateBalancesOrAudit()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Allow);

        await PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m);
        await CloseMonthAsync(host, period);

        // Act
        var act = () => CloseMonthAsync(host, period);

        // Assert
        await act
            .Should()
            .ThrowAsync<PeriodAlreadyClosedException>()
            .WithMessage($"*{period:yyyy-MM-dd}*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

            var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
            balances.Should().HaveCount(2);

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CloseMonthAsync_NegativeBalance_Forbid_FailsAndDoesNotWriteBalancesOrMarkClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Forbid);

        // Seed prior month closing balance negative for Cash (50) directly, so CloseMonth becomes the point
        // where NegativeBalancePolicy is enforced (not PostingEngine).
        // We seed 2025-12 as -100 so that 2026-01 opening is -100 and closing is also negative.
        await SeedBalanceAsync(fixture.ConnectionString, new DateOnly(2025, 12, 1), accountCode: "50", opening: 0m, closing: -100m);
        // Act
        var act = () => CloseMonthAsync(host, period);

        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            // Message comes from NegativeBalancePolicy guard. Keep it strict enough to prevent false positives,
            // but flexible enough to tolerate minor wording changes.
            .WithMessage("*Negative balance forbidden*50*period=2026-01-01*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

            var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
            balances.Should().BeEmpty();

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().BeEmpty();
        }
    }

    
    private static async Task SeedBalanceAsync(
        string connectionString,
        DateOnly period,
        string accountCode,
        decimal opening,
        decimal closing,
        CancellationToken ct = default)
    {
        const string accountIdSql =
            "SELECT account_id " +
            "FROM accounting_accounts " +
            "WHERE code = @code " +
            "LIMIT 1;";

        const string insertSql =
            "INSERT INTO accounting_balances(" +
            "period, account_id, dimension_set_id, opening_balance, closing_balance" +
            ") VALUES (" +
            "@period, @account_id, @dimension_set_id, @opening_balance, @closing_balance" +
            ");";

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        Guid accountId;
        await using (var getCmd = new NpgsqlCommand(accountIdSql, conn))
        {
            getCmd.Parameters.AddWithValue("code", accountCode);

            var accountIdObj = await getCmd.ExecuteScalarAsync(ct);
            accountIdObj.Should().NotBeNull($"account '{accountCode}' must exist before seeding balances");

            accountId = (Guid)accountIdObj!;
        }

        await using (var insertCmd = new NpgsqlCommand(insertSql, conn))
        {
            insertCmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
            insertCmd.Parameters.AddWithValue("account_id", accountId);
            insertCmd.Parameters.AddWithValue("dimension_set_id", Guid.Empty);
            insertCmd.Parameters.AddWithValue("opening_balance", opening);
            insertCmd.Parameters.AddWithValue("closing_balance", closing);

            await insertCmd.ExecuteNonQueryAsync(ct);
        }
    }

private static async Task SeedMinimalCoaAsync(IHost host, NegativeBalancePolicy cashPolicy)
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
            NegativeBalancePolicy: cashPolicy
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
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
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
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
                    period: periodUtc,
                    debit: debit,
                    credit: credit,
                    amount: amount);
            },
            ct: CancellationToken.None);
    }
}
