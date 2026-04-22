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

/// <summary>
/// P0: NegativeBalancePolicy enforcement during CloseMonth must respect account.NormalBalance.
/// For credit-normal accounts (including contra assets), a normal balance is negative (debit-credit)
/// and must NOT be treated as a forbidden negative balance.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_NegativeBalance_ContraCreditNormal_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Dec2025 = new(2025, 12, 1);
    private static readonly DateOnly Jan2026 = new(2026, 1, 1);

    [Fact]
    public async Task CloseMonthAsync_WhenContraAssetHasNormalCreditBalance_Forbid_DoesNotThrow_AndClosesPeriod()
    {
        using var host = CreateHost();

        var contraId = await CreateContraAssetAsync(host, NegativeBalancePolicy.Forbid);

        // Contra-asset is credit-normal => normal balance is NEGATIVE in stored (debit-credit) convention.
        await SeedBalanceAsync(Fixture.ConnectionString, Dec2025, contraId, opening: 0m, closing: -100m);

        await CloseMonthAsync(host, Jan2026);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var balances = await sp.GetRequiredService<IAccountingBalanceReader>()
            .GetForPeriodAsync(Jan2026, CancellationToken.None);

        balances.Should().ContainSingle(b => b.AccountId == contraId && b.ClosingBalance == -100m);

        var closed = await sp.GetRequiredService<IClosedPeriodReader>()
            .GetClosedAsync(Jan2026, Jan2026, CancellationToken.None);

        closed.Should().HaveCount(1, "CloseMonth must mark the period as closed");
    }

    [Fact]
    public async Task CloseMonthAsync_WhenContraAssetHasDebitBalance_Forbid_ShouldFailWithNegativeBalanceMessage()
    {
        using var host = CreateHost();

        var contraId = await CreateContraAssetAsync(host, NegativeBalancePolicy.Forbid);

        // Debit-balance for a credit-normal account => violation.
        await SeedBalanceAsync(Fixture.ConnectionString, Dec2025, contraId, opening: 0m, closing: 100m);

        var act = () => CloseMonthAsync(host, Jan2026);

        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance forbidden:*02*period=2026-01-01*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingBalanceReader>()
                .GetForPeriodAsync(Jan2026, CancellationToken.None))
            .Should().BeEmpty("failed closing must not write balances");

        (await sp.GetRequiredService<IClosedPeriodReader>()
                .GetClosedAsync(Jan2026, Jan2026, CancellationToken.None))
            .Should().BeEmpty("failed closing must not mark the period as closed");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<Guid> CreateContraAssetAsync(IHost host, NegativeBalancePolicy policy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Provide a minimal realistic CoA shape.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return await accounts.CreateAsync(new CreateAccountRequest(
            Code: "02",
            Name: "Accumulated Depreciation",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            IsContra: true,
            NegativeBalancePolicy: policy
        ), CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "it", CancellationToken.None);
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
