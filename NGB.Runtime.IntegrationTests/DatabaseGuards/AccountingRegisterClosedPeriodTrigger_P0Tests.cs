using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRegisterClosedPeriodTrigger_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task Trigger_Forbids_InsertIntoRegisterMain_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 10m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Forbids_UpdateOnRegisterMain_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();
        await InsertRegisterRowAsync(
            Fixture.ConnectionString,
            doc,
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            amount: 10m);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => UpdateRegisterRowAmountAsync(Fixture.ConnectionString, doc, delta: 1m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Allows_InsertIntoRegisterMain_ForOpenPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        await InsertRegisterRowAsync(
            Fixture.ConnectionString,
            doc,
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            cashId,
            revenueId,
            amount: 10m);

        (await CountRegisterRowsAsync(Fixture.ConnectionString, doc)).Should().Be(1);
    }

    private static async Task MarkPeriodClosedAsync(string cs, DateOnly period)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
            VALUES (@period, @closed_at, @closed_by);
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("closed_at", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("closed_by", "test");

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertRegisterRowAsync(
        string cs,
        Guid documentId,
        DateTime periodUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_register_main
            (document_id, period, debit_account_id, credit_account_id,
             amount, is_storno)
            VALUES
            (@document_id, @period, @debit_account_id, @credit_account_id,
             @amount, FALSE);
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("period", periodUtc);
        cmd.Parameters.AddWithValue("debit_account_id", debitAccountId);
        cmd.Parameters.AddWithValue("credit_account_id", creditAccountId);
        cmd.Parameters.AddWithValue("amount", amount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task UpdateRegisterRowAmountAsync(string cs, Guid documentId, decimal delta)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_register_main
            SET amount = amount + @delta
            WHERE document_id = @document_id;
            """, conn);

        cmd.Parameters.AddWithValue("delta", delta);
        cmd.Parameters.AddWithValue("document_id", documentId);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> CountRegisterRowsAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM accounting_register_main
            WHERE document_id = @document_id;
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);

        var scalar = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return (scalar is long l) ? l : Convert.ToInt64(scalar);
    }
}
