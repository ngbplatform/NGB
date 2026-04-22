using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_IntegrityViolation_TurnoversMismatch_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task CloseMonthAsync_WhenTurnoversDoNotMatchRegister_Throws_AndDoesNotWriteBalances_OrMarkPeriodClosed()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);

        await CorruptTurnoversAsync(Fixture.ConnectionString, Period, cashId);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(Period, closedBy: "test", CancellationToken.None);
        };

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage($"*Integrity violation: turnovers mismatch for period {Period:yyyy-MM-dd}*Mismatched keys:*");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        (await balanceReader.GetForPeriodAsync(Period, CancellationToken.None))
            .Should().BeEmpty("balances must not be written when integrity check fails");

        var closedPeriodReader = sp.GetRequiredService<IClosedPeriodReader>();
        (await closedPeriodReader.GetClosedAsync(Period, Period, CancellationToken.None))
            .Should().BeEmpty("period must not be marked closed when integrity check fails");
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

        var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        affected.Should().Be(1);
    }
}
