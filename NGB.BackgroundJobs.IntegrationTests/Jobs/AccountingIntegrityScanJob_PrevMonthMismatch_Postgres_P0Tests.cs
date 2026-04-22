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

/// <summary>
/// Covers the branch where the current month passes, but the previous month fails.
/// This ensures <see cref="AccountingIntegrityScanJob"/> updates metrics after the first scanned period.
/// </summary>
[Collection(HangfirePostgresCollection.Name)]
public sealed class AccountingIntegrityScanJob_PrevMonthMismatch_Postgres_P0Tests(HangfirePostgresFixture fixture)
{
    private static readonly Guid EmptyDimensionSetId = Guid.Empty;

    [Fact]
    public async Task RunAsync_WhenOnlyPreviousMonthHasTurnoversMismatch_FailsAfterScanningCurrentAndReportsOnePeriodScanned()
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..12].ToUpperInvariant();
        var accountId = Guid.CreateVersion7();
        var code = $"BGJOB_ACC_SCAN_PREV_{suffix}";

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateOnly(now.Year, now.Month, 1);
        var prevMonthStart = currentMonthStart.AddMonths(-1);

        await InsertAccountAsync(accountId, code);
        await InsertTurnoverAsync(prevMonthStart, accountId, debitAmount: 10m);

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
            snapshot["periods_total"].Should().Be(2);
            snapshot["periods_scanned"].Should().Be(1, "current month must be scanned successfully before failing on previous month");
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
                Name = "BGJOB integrity scan prev-only test",
                AccountType = 0, // Asset
                StatementSection = 1, // Assets
                NegativeBalancePolicy = 0 // Allow
            });
    }

    private async Task InsertTurnoverAsync(DateOnly periodMonthStart, Guid accountId, decimal debitAmount)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Dapper does not reliably support DateOnly parameter binding in all environments.
        // Send as text and cast explicitly to DATE on the PostgreSQL side.
        var period = periodMonthStart.ToString("yyyy-MM-dd");

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers (
                period,
                account_id,
                dimension_set_id,
                debit_amount,
                credit_amount
            ) VALUES (
                @Period::date,
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
            if (string.IsNullOrWhiteSpace(name) || delta == 0)
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
