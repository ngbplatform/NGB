using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.Balances;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Maintenance;
using NGB.Runtime.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Maintenance;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRebuild_VerifyFailure_RollsBack_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01
    private static readonly DateOnly PreviousPeriod = new(2025, 12, 1);
    private static readonly DateTime PrevDayUtc = new(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RebuildMonthAsync_WhenVerifyThrows_RollsBack_AndKeepsPreviousDerivedData()
    {
        await PrepareCorruptedDerivedDataAsync();

        var (turnoversBefore, balancesBefore, verifyBefore) = await ReadSnapshotAsync();

        turnoversBefore.Should().NotBeEmpty();
        balancesBefore.Should().NotBeEmpty();
        verifyBefore.IsOk.Should().BeFalse("we intentionally corrupted derived data before running rebuild");

        // Act: rebuild month with fault injected verify reader.
        using var faultyHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingConsistencyReportReader>();
                services.AddScoped<AccountingConsistencyReportService>();
                services.AddScoped<IAccountingConsistencyReportReader>(sp =>
                    new FailAfterInnerConsistencyReportReader(sp.GetRequiredService<AccountingConsistencyReportService>()));
            });

        Func<Task> act = async () =>
        {
            await using var scope = faultyHost.Services.CreateAsyncScope();
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildMonthAsync(Period, previousPeriodForChainCheck: PreviousPeriod, CancellationToken.None);
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after verify*");

        // Assert: both turnovers and balances must remain corrupted (rollback happened)
        var (turnoversAfter, balancesAfter, verifyAfter) = await ReadSnapshotAsync();

        turnoversAfter.Should().BeEquivalentTo(turnoversBefore, opts => opts.WithStrictOrdering());
        balancesAfter.Should().BeEquivalentTo(balancesBefore, opts => opts.WithStrictOrdering());
        verifyAfter.IsOk.Should().BeFalse("rollback should keep the corrupted state intact");
    }

    [Fact]
    public async Task RebuildAndVerifyAsync_WhenVerifyThrows_RollsBack_AndKeepsPreviousDerivedData()
    {
        await PrepareCorruptedDerivedDataAsync();

        var (turnoversBefore, balancesBefore, verifyBefore) = await ReadSnapshotAsync();

        turnoversBefore.Should().NotBeEmpty();
        balancesBefore.Should().NotBeEmpty();
        verifyBefore.IsOk.Should().BeFalse("we intentionally corrupted derived data before running rebuild");

        // Act: RebuildAndVerifyAsync uses RebuildMonthAsync internally, so verify failure must rollback the whole txn.
        using var faultyHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingConsistencyReportReader>();
                services.AddScoped<AccountingConsistencyReportService>();
                services.AddScoped<IAccountingConsistencyReportReader>(sp =>
                    new FailAfterInnerConsistencyReportReader(sp.GetRequiredService<AccountingConsistencyReportService>()));
            });

        Func<Task> act = async () =>
        {
            await using var scope = faultyHost.Services.CreateAsyncScope();
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildAndVerifyAsync(Period, previousPeriodForChainCheck: PreviousPeriod, CancellationToken.None);
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after verify*");

        // Assert: derived data is unchanged.
        var (turnoversAfter, balancesAfter, verifyAfter) = await ReadSnapshotAsync();

        turnoversAfter.Should().BeEquivalentTo(turnoversBefore, opts => opts.WithStrictOrdering());
        balancesAfter.Should().BeEquivalentTo(balancesBefore, opts => opts.WithStrictOrdering());
        verifyAfter.IsOk.Should().BeFalse();
    }

    private async Task PrepareCorruptedDerivedDataAsync()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Previous month closed to provide carry-forward.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), PrevDayUtc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, PreviousPeriod);

        // Current month activity; balances are created via rebuild.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildBalancesAsync(Period, CancellationToken.None);
        }

        // Corrupt BOTH derived datasets so that a successful rebuild would normally fix them.
        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);
        await CorruptBalancesAsync(Fixture.ConnectionString, Period, cashId);
    }

    private async Task<(IReadOnlyList<AccountingTurnover> turnovers, IReadOnlyList<AccountingBalance> balances, AccountingConsistencyReport report)>
        ReadSnapshotAsync()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
        var balanceReader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();
        var reportReader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();

        var turnovers = await turnoverReader.GetForPeriodAsync(Period, CancellationToken.None);
        var balances = await balanceReader.GetForPeriodAsync(Period, CancellationToken.None);
        var report = await reportReader.RunForPeriodAsync(Period, PreviousPeriod, CancellationToken.None);

        return (turnovers, balances, report);
    }

    private static async Task CorruptTurnoversAsync(string connectionString, DateOnly period, Guid accountId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_turnovers
            SET debit_amount = debit_amount + 1
            WHERE period = @period AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", Guid.Empty);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task CorruptBalancesAsync(string connectionString, DateOnly period, Guid accountId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_balances
            SET closing_balance = closing_balance + 1
            WHERE period = @period AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", Guid.Empty);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class FailAfterInnerConsistencyReportReader(IAccountingConsistencyReportReader inner)
        : IAccountingConsistencyReportReader
    {
        private bool _failed;

        public async Task<AccountingConsistencyReport> RunForPeriodAsync(
            DateOnly period,
            DateOnly? previousPeriodForChainCheck = null,
            CancellationToken ct = default)
        {
            var report = await inner.RunForPeriodAsync(period, previousPeriodForChainCheck, ct);

            if (_failed)
                return report;

            _failed = true;
            throw new NotSupportedException("Simulated failure after verify");
        }
    }
}
