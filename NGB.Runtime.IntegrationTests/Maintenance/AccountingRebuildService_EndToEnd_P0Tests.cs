using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.Balances;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Maintenance;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Maintenance;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRebuildService_EndToEnd_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01
    private static readonly DateOnly PreviousPeriod = new(2025, 12, 1);

    private static readonly DateTime PrevDayUtc = new(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RebuildTurnoversAsync_PeriodNotMonthStart_NormalizesToMonthStart()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
        var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();

        // Act: pass a mid-month date; service must rebuild the normalized month-start period.
        var written = await rebuild.RebuildTurnoversAsync(new DateOnly(2026, 1, 15), CancellationToken.None);

        // Assert
        written.Should().BeGreaterThan(0);
        var turnovers = await turnoverReader.GetForPeriodAsync(Period, CancellationToken.None);
        turnovers.Should().ContainSingle(t => t.AccountCode == "50" && t.DebitAmount == 100m && t.CreditAmount == 0m);
        turnovers.Should().ContainSingle(t => t.AccountCode == "90.1" && t.DebitAmount == 0m && t.CreditAmount == 100m);
    }

    [Fact]
    public async Task RebuildTurnoversAsync_WhenTurnoversCorrupted_RebuildsFromRegister_AndVerifyIsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc, ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);

        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
        var reportReader = scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>();
        var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();

        (await reportReader.RunForPeriodAsync(Period, null, CancellationToken.None))
            .TurnoversVsRegisterDiffCount
            .Should().BeGreaterThan(0);

        // Act
        var written = await rebuild.RebuildTurnoversAsync(Period, CancellationToken.None);

        // Assert
        written.Should().BeGreaterThan(0);

        var after = await reportReader.RunForPeriodAsync(Period, null, CancellationToken.None);
        after.IsOk.Should().BeTrue();
        after.TurnoversVsRegisterDiffCount.Should().Be(0);
        after.Issues.Should().BeEmpty();

        var turnovers = await turnoverReader.GetForPeriodAsync(Period, CancellationToken.None);
        turnovers.Should().ContainSingle(t => t.AccountCode == "50" && t.DebitAmount == 100m && t.CreditAmount == 0m);
        turnovers.Should().ContainSingle(t => t.AccountCode == "90.1" && t.DebitAmount == 0m && t.CreditAmount == 100m);
    }

    [Fact]
    public async Task RebuildTurnoversAsync_WhenWriterFailsAfterDelete_RollsBackAndKeepsOldTurnovers()
    {
        // Arrange: create correct turnovers first.
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await ReportingTestHelpers.SeedMinimalCoAAsync(host);
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);
        }

        IReadOnlyList<AccountingTurnover> before;
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            before = await scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>()
                .GetForPeriodAsync(Period, CancellationToken.None);
            before.Should().NotBeEmpty();
        }

        // Act: run rebuild with fault injection (throw AFTER delete).
        using var faultyHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingTurnoverWriter>();
                services.AddScoped<PostgresAccountingTurnoverWriter>();
                services.AddScoped<IAccountingTurnoverWriter>(sp =>
                    new FailAfterDeleteTurnoverWriter(sp.GetRequiredService<PostgresAccountingTurnoverWriter>()));
            });

        Func<Task> act = async () =>
        {
            await using var scope = faultyHost.Services.CreateAsyncScope();
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildTurnoversAsync(Period, CancellationToken.None);
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after turnovers delete*");

        // Assert: old rows must still exist (rollback).
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var after = await scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>()
                .GetForPeriodAsync(Period, CancellationToken.None);

            after.Should().BeEquivalentTo(before, opts => opts.WithStrictOrdering());
        }
    }

    [Fact]
    public async Task RebuildBalancesAsync_HappyPath_ComputesFromPreviousBalancesAndCurrentTurnovers()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Previous period: create balances by closing.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), PrevDayUtc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, PreviousPeriod);

        // Current period: only turnovers exist (open month).
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
        var balanceReader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();

        (await balanceReader.GetForPeriodAsync(Period, CancellationToken.None))
            .Should().BeEmpty("open period has no balances until rebuilt/closed");

        // Act
        var written = await rebuild.RebuildBalancesAsync(Period, CancellationToken.None);

        // Assert
        written.Should().BeGreaterThan(0);

        var balances = await balanceReader.GetForPeriodAsync(Period, CancellationToken.None);

        balances.Should().ContainSingle(b => b.AccountCode == "50" && b.OpeningBalance == 100m && b.ClosingBalance == 150m);
        balances.Should().ContainSingle(b => b.AccountCode == "90.1" && b.OpeningBalance == -100m && b.ClosingBalance == -150m);
    }

    [Fact]
    public async Task RebuildBalancesAsync_WhenWriterFailsAfterDelete_RollsBackAndKeepsOldBalances()
    {
        // Arrange: create balances in open period by running rebuild once.
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await ReportingTestHelpers.SeedMinimalCoAAsync(host);
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), PrevDayUtc, "50", "90.1", 100m);
            await ReportingTestHelpers.CloseMonthAsync(host, PreviousPeriod);
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);

            await using var scope = host.Services.CreateAsyncScope();
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildBalancesAsync(Period, CancellationToken.None);
        }

        IReadOnlyList<AccountingBalance> before;
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            before = await scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>()
                .GetForPeriodAsync(Period, CancellationToken.None);
            before.Should().NotBeEmpty();
        }

        // Act: run rebuild with fault injection (throw AFTER delete).
        using var faultyHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingBalanceWriter>();
                services.AddScoped<PostgresAccountingBalanceWriter>();
                services.AddScoped<IAccountingBalanceWriter>(sp =>
                    new FailAfterDeleteBalanceWriter(sp.GetRequiredService<PostgresAccountingBalanceWriter>()));
            });

        Func<Task> act = async () =>
        {
            await using var scope = faultyHost.Services.CreateAsyncScope();
            var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();
            await rebuild.RebuildBalancesAsync(Period, CancellationToken.None);
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after balances delete*");

        // Assert: old rows must still exist (rollback).
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var after = await scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>()
                .GetForPeriodAsync(Period, CancellationToken.None);

            after.Should().BeEquivalentTo(before, opts => opts.WithStrictOrdering());
        }
    }

    [Fact]
    public async Task RebuildMonthAsync_HappyPath_RebuildsTurnoversAndBalances_AndRunsVerifyInsideTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Previous month closed to provide carry-forward.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), PrevDayUtc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, PreviousPeriod);

        // Current month activity + corrupt turnovers.
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 50m);
        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);

        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();

        // Act
        var result = await rebuild.RebuildMonthAsync(Period, previousPeriodForChainCheck: PreviousPeriod, CancellationToken.None);

        // Assert
        result.Period.Should().Be(Period);
        result.TurnoverRowsWritten.Should().BeGreaterThan(0);
        result.BalanceRowsWritten.Should().BeGreaterThan(0);

        result.VerifyReport.IsOk.Should().BeTrue();
        result.VerifyReport.TurnoversVsRegisterDiffCount.Should().Be(0);
        result.VerifyReport.BalanceVsTurnoverMismatchCount.Should().Be(0);
        result.VerifyReport.BalanceChainMismatchCount.Should().Be(0);
        result.VerifyReport.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Rebuild_IsForbidden_ForClosedPeriods()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create some data then close.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, Period);

        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();

        var act1 = () => rebuild.RebuildTurnoversAsync(Period, CancellationToken.None);
        var act2 = () => rebuild.RebuildBalancesAsync(Period, CancellationToken.None);
        var act3 = () => rebuild.RebuildMonthAsync(Period, null, CancellationToken.None);
        var act4 = () => rebuild.RebuildAndVerifyAsync(Period, null, CancellationToken.None);

        await act1.Should().ThrowAsync<AccountingRebuildPeriodClosedException>()
            .WithMessage($"*Rebuild is forbidden. Period is closed: {Period:yyyy-MM-dd}*");
        await act2.Should().ThrowAsync<AccountingRebuildPeriodClosedException>()
            .WithMessage($"*Rebuild is forbidden. Period is closed: {Period:yyyy-MM-dd}*");
        await act3.Should().ThrowAsync<AccountingRebuildPeriodClosedException>()
            .WithMessage($"*Rebuild is forbidden. Period is closed: {Period:yyyy-MM-dd}*");
        await act4.Should().ThrowAsync<AccountingRebuildPeriodClosedException>()
            .WithMessage($"*Rebuild is forbidden. Period is closed: {Period:yyyy-MM-dd}*");
    }

    private static async Task CorruptTurnoversAsync(string connectionString, DateOnly period, Guid accountId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_turnovers
            SET debit_amount = debit_amount + 1
            WHERE period = @period AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", Guid.Empty);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class FailAfterDeleteTurnoverWriter(IAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        private bool _failed;

        public async Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
        {
            await inner.DeleteForPeriodAsync(period, ct);

            if (_failed)
                return;

            _failed = true;
            throw new NotSupportedException("Simulated failure after turnovers delete");
        }

        public Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default) =>
            inner.WriteAsync(turnovers, ct);
    }

    private sealed class FailAfterDeleteBalanceWriter(IAccountingBalanceWriter inner) : IAccountingBalanceWriter
    {
        private bool _failed;

        public async Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
        {
            await inner.DeleteForPeriodAsync(period, ct);

            if (_failed)
                return;

            _failed = true;
            throw new NotSupportedException("Simulated failure after balances delete");
        }

        public Task SaveAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct = default) =>
            inner.SaveAsync(balances, ct);
    }
}
