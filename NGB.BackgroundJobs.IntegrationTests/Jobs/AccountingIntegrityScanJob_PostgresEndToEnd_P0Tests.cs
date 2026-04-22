using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.Checkers;
using NGB.PostgreSql.DependencyInjection;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class AccountingIntegrityScanJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    private static readonly Guid EmptyDimensionSetId = Guid.Empty;

    [Fact]
    public async Task RunAsync_WhenLedgerIsEmpty_SucceedsAndReportsAllPeriodsScanned()
    {
        using var sp = BuildServiceProvider(fixture.ConnectionString);

        var metrics = new TestJobRunMetrics();

        await using var scope = sp.CreateAsyncScope();

        var checker = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityChecker>();

        var job = new AccountingIntegrityScanJob(
            checker,
            NullLogger<AccountingIntegrityScanJob>.Instance,
            metrics);

        await job.RunAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.Should().ContainKey("periods_total");
        snapshot["periods_total"].Should().Be(2);
        snapshot["periods_scanned"].Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_WhenTurnoversDriftExists_ThrowsAndReportsZeroPeriodsScanned()
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..12].ToUpperInvariant();
        var accountId = Guid.CreateVersion7();
        var code = $"BGJOB_ACC_SCAN_{suffix}";

        // Create drift for a small window that covers a possible month boundary between insert and job start.
        var now = DateTime.UtcNow;
        var p0 = new DateOnly(now.Year, now.Month, 1);
        var pMinus1 = p0.AddMonths(-1);
        var pPlus1 = p0.AddMonths(1);

        await InsertAccountAsync(accountId, code);
        await InsertTurnoverAsync(pMinus1, accountId, debitAmount: 10m);
        await InsertTurnoverAsync(p0, accountId, debitAmount: 20m);
        await InsertTurnoverAsync(pPlus1, accountId, debitAmount: 30m);

        try
        {
            using var sp = BuildServiceProvider(fixture.ConnectionString);

            var metrics = new TestJobRunMetrics();

            await using var scope = sp.CreateAsyncScope();

            var checker = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityChecker>();

            var job = new AccountingIntegrityScanJob(
                checker,
                NullLogger<AccountingIntegrityScanJob>.Instance,
                metrics);

            var act = async () => await job.RunAsync(CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*turnovers mismatch*");

            var snapshot = metrics.Snapshot();
            snapshot.Should().ContainKey("periods_total");
            snapshot["periods_total"].Should().Be(2);

            // We ensure drift for both possible current months; the job must fail on the very first period.
            snapshot["periods_scanned"].Should().Be(0);
        }
        finally
        {
            await CleanupAsync(accountId);
        }
    }

    private async Task InsertAccountAsync(Guid accountId, string code)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Minimal account row (only required columns). Enums are stored as smallint.
        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_accounts (
                account_id,
                code,
                name,
                account_type,
                statement_section,
                is_contra,
                negative_balance_policy,
                is_active,
                is_deleted
            ) VALUES (
                @AccountId,
                @Code,
                @Name,
                @AccountType,
                @StatementSection,
                FALSE,
                @NegativeBalancePolicy,
                TRUE,
                FALSE
            );
            """,
            new
            {
                AccountId = accountId,
                Code = code,
                Name = "BGJOB integrity scan test",
                AccountType = 0, // Asset
                StatementSection = 1, // Assets
                NegativeBalancePolicy = 0 // Allow
            });
    }

    private async Task InsertTurnoverAsync(DateOnly period, Guid accountId, decimal debitAmount)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers (
                period,
                account_id,
                dimension_set_id,
                debit_amount,
                credit_amount
            ) VALUES (
                @Period,
                @AccountId,
                @DimensionSetId,
                @DebitAmount,
                0
            );
            """,
            new
            {
                Period = period,
                AccountId = accountId,
                DimensionSetId = EmptyDimensionSetId,
                DebitAmount = debitAmount
            });
    }

    private async Task CleanupAsync(Guid accountId)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            DELETE FROM accounting_turnovers
            WHERE account_id = @AccountId;

            DELETE FROM accounting_accounts
            WHERE account_id = @AccountId;
            """,
            new { AccountId = accountId });
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddNgbPostgres(connectionString);

        return services.BuildServiceProvider();
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();

            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
