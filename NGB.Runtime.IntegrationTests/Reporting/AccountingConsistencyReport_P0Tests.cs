using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingConsistencyReport_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid EmptyDimensionSetId = Guid.Empty;

    [Fact]
    public async Task ConsistencyReport_HappyPath_AfterCloseMonth_IsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        await ReportingTestHelpers.PostAsync(host, doc1, ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, doc2, ReportingTestHelpers.Day1Utc, "91", "50", 40m);

        await ReportingTestHelpers.CloseMonthAsync(host, ReportingTestHelpers.Period);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();

        var report = await reader.RunForPeriodAsync(
            ReportingTestHelpers.Period,
            previousPeriodForChainCheck: null,
            CancellationToken.None);

        report.IsOk.Should().BeTrue();
        report.TurnoversVsRegisterDiffCount.Should().Be(0);
        report.BalanceVsTurnoverMismatchCount.Should().Be(0);
        report.BalanceChainMismatchCount.Should().Be(0);
        report.MissingKeyCount.Should().Be(0);
        report.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsistencyReport_Detects_TurnoversVsRegisterMismatch_WhenTurnoversCorrupted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);

        // Corrupt stored turnovers: bump debit_amount for cash key.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            await using var cmd = new NpgsqlCommand("""
                UPDATE accounting_turnovers
                SET debit_amount = debit_amount + 1
                WHERE period = @period AND account_id = @account_id AND dimension_set_id = @dimension_set_id
                """, conn);

            cmd.Parameters.AddWithValue("period", ReportingTestHelpers.Period);
            cmd.Parameters.AddWithValue("account_id", cashId);
            cmd.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

            var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            affected.Should().Be(1);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();

        var report = await reader.RunForPeriodAsync(ReportingTestHelpers.Period, null, CancellationToken.None);

        report.IsOk.Should().BeFalse();
        report.TurnoversVsRegisterDiffCount.Should().BeGreaterThan(0);
        report.Issues.Should().Contain(i => i.Kind == AccountingConsistencyIssueKind.TurnoversVsRegisterMismatch);
    }

    [Fact]
    public async Task ConsistencyReport_Detects_BalanceVsTurnoverMismatch_WhenBalancesCorrupted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);

        // NOTE:
        // With defense-in-depth guards, closed periods must be immutable, including derived tables.
        // This test only needs a balance snapshot to exist, so we seed one directly (without closing)
        // and then corrupt it.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            // Seed a correct snapshot first.
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES (@period, @account_id, @dimension_set_id, 0, 100);
                """, conn))
            {
                seed.Parameters.AddWithValue("period", ReportingTestHelpers.Period);
                seed.Parameters.AddWithValue("account_id", cashId);
                seed.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

                var affectedSeed = await seed.ExecuteNonQueryAsync(CancellationToken.None);
                affectedSeed.Should().Be(1);
            }

            // Corrupt stored balances: bump closing_balance for cash key.
            await using var cmd = new NpgsqlCommand("""
                UPDATE accounting_balances
                SET closing_balance = closing_balance + 1
                WHERE period = @period AND account_id = @account_id AND dimension_set_id = @dimension_set_id
                """, conn);

            cmd.Parameters.AddWithValue("period", ReportingTestHelpers.Period);
            cmd.Parameters.AddWithValue("account_id", cashId);
            cmd.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

            var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            affected.Should().Be(1);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();

        var report = await reader.RunForPeriodAsync(ReportingTestHelpers.Period, null, CancellationToken.None);

        report.IsOk.Should().BeFalse();
        report.BalanceVsTurnoverMismatchCount.Should().BeGreaterThan(0);
        report.Issues.Should().Contain(i => i.Kind == AccountingConsistencyIssueKind.BalanceVsTurnoverMismatch);
    }

    [Fact]
    public async Task ConsistencyReport_Detects_BalanceChainMismatch_WhenNextPeriodOpeningDoesNotMatchPreviousClosing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        // NOTE:
        // With defense-in-depth guards, closed periods (and their derived tables) are immutable.
        // For this report, we only need two balance snapshots to exist.
        // So we seed both snapshots directly and then break the chain by mutating the NEXT period.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            // Previous period: closing balance = 100.
            await using (var seedPrev = new NpgsqlCommand("""
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES (@period, @account_id, @dimension_set_id, 0, 100);
                """, conn))
            {
                seedPrev.Parameters.AddWithValue("period", ReportingTestHelpers.Period);
                seedPrev.Parameters.AddWithValue("account_id", cashId);
                seedPrev.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

                var affectedSeedPrev = await seedPrev.ExecuteNonQueryAsync(CancellationToken.None);
                affectedSeedPrev.Should().Be(1);
            }

            // Next period: opening must equal previous closing, and with no turnovers it should close the same.
            await using (var seedNext = new NpgsqlCommand("""
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES (@period, @account_id, @dimension_set_id, 100, 100);
                """, conn))
            {
                seedNext.Parameters.AddWithValue("period", ReportingTestHelpers.NextPeriod);
                seedNext.Parameters.AddWithValue("account_id", cashId);
                seedNext.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

                var affectedSeedNext = await seedNext.ExecuteNonQueryAsync(CancellationToken.None);
                affectedSeedNext.Should().Be(1);
            }

            // Break chain: mutate opening_balance in next period for cash key.
            // Also mutate closing_balance to keep BalanceVsTurnoverMismatch from triggering (no turnovers => expected closing == opening).
            await using var cmd = new NpgsqlCommand("""
                UPDATE accounting_balances
                SET opening_balance = opening_balance + 1,
                    closing_balance = closing_balance + 1
                WHERE period = @period AND account_id = @account_id AND dimension_set_id = @dimension_set_id
                """, conn);

            cmd.Parameters.AddWithValue("period", ReportingTestHelpers.NextPeriod);
            cmd.Parameters.AddWithValue("account_id", cashId);
            cmd.Parameters.AddWithValue("dimension_set_id", EmptyDimensionSetId);

            var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            affected.Should().Be(1);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();

        var report = await reader.RunForPeriodAsync(
            ReportingTestHelpers.NextPeriod,
            previousPeriodForChainCheck: ReportingTestHelpers.Period,
            CancellationToken.None);

        report.IsOk.Should().BeFalse();
        report.BalanceChainMismatchCount.Should().BeGreaterThan(0);
        report.Issues.Should().Contain(i => i.Kind == AccountingConsistencyIssueKind.BalanceChainMismatch);
    }
}
