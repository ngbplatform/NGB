using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P0: Semantics of IAccountingOperationalBalanceReader are critical for
/// NegativeBalancePolicy enforcement under concurrency.
/// </summary>
[Collection(PlatformPostgresCollection.Name)]
public sealed class AccountingOperationalBalanceReader_Semantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CashCode = "50";
    private const string RevenueCode = "90.1";

    [Fact]
    public async Task GetForKeysAsync_PriorOpenDelta_RespectsClosedBoundaryMonthAndDimensions()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var otherSet = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@OtherSet);
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES ('2026-01-01', @CashId, @EmptySet, 0, 100);
                INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
                VALUES
                    ('2025-12-01', @CashId, @EmptySet, 999, 0),
                    ('2026-01-01', @CashId, @EmptySet, 100, 0),
                    ('2026-02-01', @CashId, @EmptySet, 30, 10),
                    ('2026-03-01', @CashId, @EmptySet, 5, 2),
                    ('2026-04-01', @CashId, @EmptySet, 7, 1),
                    ('2026-05-01', @CashId, @EmptySet, 1000, 0),
                    ('2026-02-01', @CashId, @OtherSet, 888, 0),
                    ('2026-02-01', @RevenueId, @EmptySet, 777, 0);
                INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
                VALUES ('2026-01-01', '2026-02-01T00:00:00Z', 'test');
                """,
                new { CashId = cashId, RevenueId = revenueId, OtherSet = otherSet, EmptySet = Guid.Empty });
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingOperationalBalanceReader>();
        var rows = await reader.GetForKeysAsync(
            new DateOnly(2026, 4, 1),
            [new AccountingBalanceKey(cashId, Guid.Empty), new AccountingBalanceKey(cashId, otherSet)]);

        var empty = rows.Should().ContainSingle(row => row.DimensionSetId == Guid.Empty).Subject;
        empty.PreviousClosingBalance.Should().Be(100m);
        empty.PriorOpenPeriodDelta.Should().Be(23m);
        empty.DebitTurnover.Should().Be(7m);
        empty.CreditTurnover.Should().Be(1m);
        var other = rows.Should().ContainSingle(row => row.DimensionSetId == otherSet).Subject;
        other.PreviousClosingBalance.Should().Be(0m);
        other.PriorOpenPeriodDelta.Should().Be(888m);
        other.DebitTurnover.Should().Be(0m);
        other.CreditTurnover.Should().Be(0m);
    }

    [Fact]
    public async Task GetForKeysAsync_WhenNoClosedPeriods_ReturnsZeroPreviousClosing_AndMonthTurnovers()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid cashId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            cashId = await accounts.CreateAsync(new CreateAccountRequest(
                CashCode,
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        var period = new DateOnly(2026, 1, 1);

        // Seed current-month turnovers directly.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount) VALUES (@P, @A, @S, @D, @C);",
                new { P = period, A = cashId, S = Guid.Empty, D = 10m, C = 3m });
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountingOperationalBalanceReader>();

            var keys = new[] { new AccountingBalanceKey(cashId, Guid.Empty) };
            var rows = await reader.GetForKeysAsync(period, keys, CancellationToken.None);

            rows.Should().HaveCount(1);
            var s = rows[0];
            s.Period.Should().Be(period);
            s.AccountId.Should().Be(cashId);
            s.DimensionSetId.Should().Be(Guid.Empty);
            s.PreviousClosingBalance.Should().Be(0m);
            s.DebitTurnover.Should().Be(10m);
            s.CreditTurnover.Should().Be(3m);
        }
    }

    [Fact]
    public async Task GetForKeysAsync_UsesMaxClosedPeriodLessOrEqual_ToPickPreviousClosingBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid cashId;
        Guid revenueId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            cashId = await accounts.CreateAsync(new CreateAccountRequest(
                CashCode,
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            revenueId = await accounts.CreateAsync(new CreateAccountRequest(
                RevenueCode,
                "Revenue",
                AccountType.Income,
                StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        var period = new DateOnly(2026, 1, 1);
        var closed1 = new DateOnly(2025, 11, 1);
        var closed2 = new DateOnly(2025, 12, 1);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // IMPORTANT:
            // Since we now have defense-in-depth closed-period triggers for balances/turnovers,
            // we must seed balances FIRST and only then mark the period as closed.

            // Balances for both months; only the latest should be selected.
            await conn.ExecuteAsync(
                "INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance) VALUES (@P, @A, @S, 0, @C);",
                new { P = closed1, A = cashId, S = Guid.Empty, C = 10m });
            await conn.ExecuteAsync(
                "INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance) VALUES (@P, @A, @S, 0, @C);",
                new { P = closed2, A = cashId, S = Guid.Empty, C = 100m });

            // Two closed periods exist; reader must use the latest (MAX) <= period.
            await conn.ExecuteAsync(
                "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@P, @At, 'it');",
                new { P = closed1, At = new DateTime(2025, 11, 30, 0, 0, 0, DateTimeKind.Utc) });

            await conn.ExecuteAsync(
                "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@P, @At, 'it');",
                new { P = closed2, At = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc) });

            // Current month turnovers.
            await conn.ExecuteAsync(
                "INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount) VALUES (@P, @A, @S, @D, @C);",
                new { P = period, A = cashId, S = Guid.Empty, D = 20m, C = 30m });

            // Irrelevant other account to ensure join is key-based.
            await conn.ExecuteAsync(
                "INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount) VALUES (@P, @A, @S, @D, @C);",
                new { P = period, A = revenueId, S = Guid.Empty, D = 999m, C = 0m });
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountingOperationalBalanceReader>();
            var keys = new[] { new AccountingBalanceKey(cashId, Guid.Empty) };

            var rows = await reader.GetForKeysAsync(period, keys, CancellationToken.None);
            rows.Should().HaveCount(1);

            var s = rows[0];
            s.PreviousClosingBalance.Should().Be(100m, "must use the latest closed period <= the requested period");
            s.DebitTurnover.Should().Be(20m);
            s.CreditTurnover.Should().Be(30m);
        }
    }

    [Fact]
    public async Task GetForKeysAsync_RespectsNullDimensions_UsingGuidEmptyKeys_ButReturnsNullsInResult()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid cashId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            cashId = await accounts.CreateAsync(new CreateAccountRequest(
                CashCode,
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        var period = new DateOnly(2026, 1, 1);
        var closed = new DateOnly(2025, 12, 1);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            // IMPORTANT: seed balances BEFORE closing the period (defense-in-depth triggers).
            await conn.ExecuteAsync(
                "INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance) VALUES (@P, @A, @S, 0, 42);",
                new { P = closed, A = cashId, S = Guid.Empty });

            await conn.ExecuteAsync(
                "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@P, @At, 'it');",
                new { P = closed, At = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc) });

            await conn.ExecuteAsync(
                "INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount) VALUES (@P, @A, @S, 5, 7);",
                new { P = period, A = cashId, S = Guid.Empty });
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountingOperationalBalanceReader>();
            var key = new AccountingBalanceKey(cashId, Guid.Empty);

            var rows = await reader.GetForKeysAsync(period, new[] { key }, CancellationToken.None);
            rows.Should().HaveCount(1);

            var s = rows[0];
            s.DimensionSetId.Should().Be(Guid.Empty);
            s.PreviousClosingBalance.Should().Be(42m);
            s.DebitTurnover.Should().Be(5m);
            s.CreditTurnover.Should().Be(7m);
        }
    }
}
