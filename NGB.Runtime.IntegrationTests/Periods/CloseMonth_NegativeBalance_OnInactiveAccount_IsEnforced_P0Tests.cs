using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounting;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_NegativeBalance_OnInactiveAccount_IsEnforced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonthAsync_WhenInactiveAccountHasNegativeBalance_Forbid_ShouldFailWithNegativeBalanceMessage_NotAccountNotFound()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var host = CreateHost();

        var period = new DateOnly(2026, 1, 1);

        var cashId = await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Forbid);

        // Deactivate the account AFTER it exists (and potentially after it has movements).
        await SetActiveAsync(host, cashId, isActive: false);

        // Seed prior month balance negative, so CloseMonth becomes the enforcement point.
        await SeedBalanceAsync(Fixture.ConnectionString, new DateOnly(2025, 12, 1), cashId, opening: 0m, closing: -100m);

        // Act
        var act = () => CloseMonthAsync(host, period);

        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance forbidden*50*period=2026-01-01*");

        // And ensure there were no writes.
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

    [Fact]
    public async Task CloseMonthAsync_WhenInactiveAccountHasNegativeBalance_Warn_ShouldClose_AndWriteBalances_AndMarkClosed()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var host = CreateHost();

        var period = new DateOnly(2026, 1, 1);

        var cashId = await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Warn);
        await SetActiveAsync(host, cashId, isActive: false);

        await SeedBalanceAsync(Fixture.ConnectionString, new DateOnly(2025, 12, 1), cashId, opening: 0m, closing: -100m);

        // Act (Warn policy => no exception expected)
        await CloseMonthAsync(host, period);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

            var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
            balances.Should().ContainSingle(b => b.AccountId == cashId && b.ClosingBalance == -100m);

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().HaveCount(1);
        }
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<Guid> SeedMinimalCoaAsync(IHost host, NegativeBalancePolicy cashPolicy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: cashPolicy
        ), CancellationToken.None);

        // P&L accounts are not required for this test, but keep CoA realistic.
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

        return cashId;
    }

    private static async Task SetActiveAsync(IHost host, Guid accountId, bool isActive)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        await mgmt.SetActiveAsync(accountId, isActive, CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private static async Task SeedBalanceAsync(
        string connectionString,
        DateOnly period,
        Guid accountId,
        decimal opening,
        decimal closing,
        CancellationToken ct = default)
    {
        const string insertSql =
            "INSERT INTO accounting_balances(" +
            "period, account_id, dimension_set_id, opening_balance, closing_balance" +
            ") VALUES (" +
            "@period, @account_id, @dimension_set_id, @opening_balance, @closing_balance" +
            ");";

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(insertSql, new
        {
            period = period.ToDateTime(TimeOnly.MinValue),
            account_id = accountId,
            dimension_set_id = Guid.Empty,
            opening_balance = opening,
            closing_balance = closing
        });
    }
}
