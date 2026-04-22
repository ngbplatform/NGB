using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: strict DB constraints for operational_register_finalizations.
/// The table is critical for Dirty/Finalized month lifecycle, so we enforce invariants at the DB level.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizations_Constraints_Enforced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InvalidRowsAreRejectedByCheckConstraints()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = await CreateRegisterAsync(host, "rr_" + Guid.CreateVersion7().ToString("N")[..8]);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var month = new DateOnly(2026, 1, 1);
        var notMonthStart = new DateOnly(2026, 1, 15);
        var nowUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // 1) status must be 1..3.
        // Note: invalid status can also violate ck_opreg_finalizations_consistent_timestamps (because the OR clauses don't match).
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_finalizations (register_id, period, status)
                    VALUES (@R, @P, @S);
                    """,
                    new { R = registerId, P = month, S = 9 }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().BeOneOf(
                "ck_opreg_finalizations_status",
                "ck_opreg_finalizations_consistent_timestamps");

            // And the status constraint must exist in schema.
            var exists = await conn.QuerySingleAsync<int>(
                "SELECT 1 FROM pg_constraint WHERE conname = 'ck_opreg_finalizations_status';");
            exists.Should().Be(1);
        }

        // 2) period must be month-start (provide a valid Dirty shape so only month-start fails).
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_finalizations (register_id, period, status, dirty_since_utc)
                    VALUES (@R, @P, 2, @DirtySince);
                    """,
                    new { R = registerId, P = notMonthStart, DirtySince = nowUtc }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_finalizations_period_is_month_start");
        }

        // 3) consistent timestamps: status=Finalized requires finalized_at_utc and forbids dirty/blocked fields.
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_finalizations (register_id, period, status)
                    VALUES (@R, @P, 1);
                    """,
                    new { R = registerId, P = month }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_finalizations_consistent_timestamps");
        }

        // 4) consistent timestamps: status=Dirty requires dirty_since_utc.
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_finalizations (register_id, period, status)
                    VALUES (@R, @P, 2);
                    """,
                    new { R = registerId, P = month }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_finalizations_consistent_timestamps");
        }

        // 5) consistent timestamps: status=BlockedNoProjector requires blocked_since_utc + blocked_reason.
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO operational_register_finalizations (register_id, period, status, blocked_since_utc)
                    VALUES (@R, @P, 3, @BlockedSince);
                    """,
                    new { R = registerId, P = month, BlockedSince = nowUtc }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.ConstraintName.Should().Be("ck_opreg_finalizations_consistent_timestamps");
        }
    }

    [Fact]
    public async Task ValidRowsAreAccepted_AndPrimaryKeyIsEnforced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = await CreateRegisterAsync(host, "rr_" + Guid.CreateVersion7().ToString("N")[..8]);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var month = new DateOnly(2026, 2, 1);
        var finalizedAt = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc);

        // Finalized row is valid.
        await conn.ExecuteAsync(
            """
            INSERT INTO operational_register_finalizations (register_id, period, status, finalized_at_utc)
            VALUES (@R, @P, 1, @FinalizedAt);
            """,
            new { R = registerId, P = month, FinalizedAt = finalizedAt });

        // PK must be enforced (second insert for same register+month).
        var ex = await FluentActions
            .Invoking(() => conn.ExecuteAsync(
                """
                INSERT INTO operational_register_finalizations (register_id, period, status, dirty_since_utc)
                VALUES (@R, @P, 2, @DirtySince);
                """,
                new { R = registerId, P = month, DirtySince = finalizedAt }))
            .Should().ThrowAsync<PostgresException>();

        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("pk_opreg_finalizations");
    }

    private static async Task<Guid> CreateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: "IT Register", CancellationToken.None);
    }
}
