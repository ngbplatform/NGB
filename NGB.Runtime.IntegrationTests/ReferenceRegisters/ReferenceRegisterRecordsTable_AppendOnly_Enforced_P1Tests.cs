using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P1: per-register records tables (refreg_*__records) are append-only.
/// Verifies enforcement behavior: UPDATE/DELETE are rejected by the shared
/// guard function ngb_forbid_mutation_of_append_only_table().
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsTable_AppendOnly_Enforced_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RecordsTable_IsAppendOnly_UpdateAndDeleteAreForbidden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Keep it simple: non-periodic + independent (period/recorder columns are nullable).
        Guid registerId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code: "rr_append_only",
                name: "RR AppendOnly",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(registerId, [], CancellationToken.None);
            await mgmt.ReplaceFieldsAsync(registerId, [], CancellationToken.None);
        }

        // Ensure physical schema exists.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        // Resolve physical table name.
        string table;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();

            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        // Insert one record (INSERT is allowed). We do not supply record_id (BIGSERIAL).
        long recordId;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            recordId = await conn.ExecuteScalarAsync<long>(
                $"""
                INSERT INTO {table} (dimension_set_id, recorded_at_utc, is_deleted)
                VALUES (@DimSetId, @RecordedAtUtc, FALSE)
                RETURNING record_id;
                """,
                new
                {
                    DimSetId = Guid.Empty,
                    RecordedAtUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)
                });
        }

        // UPDATE must be rejected.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"UPDATE {table} SET recorded_at_utc = recorded_at_utc WHERE record_id = @Id;",
                    new { Id = recordId },
                    tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            ex.Which.MessageText.Should().Contain("Append-only table cannot be mutated");

            await tx.RollbackAsync(CancellationToken.None);
        }

        // DELETE must be rejected.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"DELETE FROM {table} WHERE record_id = @Id;",
                    new { Id = recordId },
                    tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            ex.Which.MessageText.Should().Contain("Append-only table cannot be mutated");

            await tx.RollbackAsync(CancellationToken.None);
        }

        // Verify: row still exists and value did not change.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM {table} WHERE record_id = @Id;",
                new { Id = recordId });

            count.Should().Be(1);

            var isDeleted = await conn.ExecuteScalarAsync<bool>(
                $"SELECT is_deleted FROM {table} WHERE record_id = @Id;",
                new { Id = recordId });

            isDeleted.Should().BeFalse();
        }
    }
}
