using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Checkers;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Maintenance;

[Collection(PostgresCollection.Name)]
public sealed class AccountingIntegrityChecker_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task DiagnosticsAndChecker_OnCleanData_ReturnZeroDiff_AndDoNotThrow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await using var scope = host.Services.CreateAsyncScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityDiagnostics>();
        var checker = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityChecker>();

        var diff = await diagnostics.GetTurnoversVsRegisterDiffCountAsync(Period, CancellationToken.None);
        diff.Should().Be(0);

        var act = () => checker.AssertPeriodIsBalancedAsync(Period, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Diagnostics_WhenTurnoversAreCorrupted_ReturnsPositiveDiffCount()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);

        await using var scope = host.Services.CreateAsyncScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityDiagnostics>();

        var diff = await diagnostics.GetTurnoversVsRegisterDiffCountAsync(Period, CancellationToken.None);
        diff.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Checker_WhenTurnoversAreCorrupted_ThrowsWithMismatchedKeysCount()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);

        await using var scope = host.Services.CreateAsyncScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityDiagnostics>();
        var checker = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityChecker>();

        var diff = await diagnostics.GetTurnoversVsRegisterDiffCountAsync(Period, CancellationToken.None);
        diff.Should().BeGreaterThan(0);

        var act = () => checker.AssertPeriodIsBalancedAsync(Period, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage($"*Integrity violation: turnovers mismatch for period {Period:yyyy-MM-dd}*Mismatched keys: {diff}*");
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

        // Use DateTime parameter because some environments may not map DateOnly automatically.
        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", Guid.Empty);

        var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        affected.Should().Be(1);
    }
}
