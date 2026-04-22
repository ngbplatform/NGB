using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: per-register records tables must enforce semantic constraints at the DB level
/// (defense-in-depth against accidental raw SQL inserts / buggy writers).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsTable_SemanticConstraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task IndependentNonPeriodic_EnforcesNullRecorder_AndNullPeriodColumns()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        Guid existingDocId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            registerId = await mgmt.UpsertAsync(
                code: "rr_sem_np_ind",
                name: "RR Semantics NP/Ind",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(registerId, [], CancellationToken.None);
            await mgmt.ReplaceFieldsAsync(registerId, [], CancellationToken.None);

            existingDocId = await drafts.CreateDraftAsync(
                typeCode: "test_doc",
                number: "RR-SEM-1",
                dateUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                manageTransaction: true,
                ct: CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();

            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        // Assert: check constraints exist on the table.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var checks = (await conn.QueryAsync<(string Name, string Def)>(
                """
                SELECT conname AS Name, pg_get_constraintdef(oid) AS Def
                FROM pg_constraint
                WHERE conrelid = @Table::regclass
                  AND contype = 'c';
                """,
                new { Table = table })).AsList();

            checks.Should().NotBeEmpty();

            checks.Any(x => x.Def.Contains("recorder_document_id IS NULL", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue("Independent registers must enforce recorder_document_id IS NULL at the DB level");

            checks.Any(x =>
                    x.Def.Contains("period_utc IS NULL", StringComparison.OrdinalIgnoreCase)
                    && x.Def.Contains("period_bucket_utc IS NULL", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue("NonPeriodic registers must enforce NULL period columns at the DB level");
        }

        // DB-level enforcement: recorder_document_id must be NULL (even if it references a valid document).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"""
                    INSERT INTO {table} (dimension_set_id, recorder_document_id, recorded_at_utc, is_deleted)
                    VALUES (@DimSetId, @RecorderId, @RecordedAtUtc, FALSE);
                    """,
                    new
                    {
                        DimSetId = Guid.Empty,
                        RecorderId = existingDocId,
                        RecordedAtUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)
                    });
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("23514");
        }

        // DB-level enforcement: period columns must be NULL for NonPeriodic registers.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var dt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"""
                    INSERT INTO {table} (dimension_set_id, period_utc, period_bucket_utc, recorded_at_utc, is_deleted)
                    VALUES (@DimSetId, @PeriodUtc, @BucketUtc, @RecordedAtUtc, FALSE);
                    """,
                    new
                    {
                        DimSetId = Guid.Empty,
                        PeriodUtc = dt,
                        BucketUtc = dt,
                        RecordedAtUtc = dt
                    });
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("23514");
        }
    }

    [Fact]
    public async Task SubordinateMonthly_HasNoNullChecks_AndRequiresNotNullColumns()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        Guid docId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            registerId = await mgmt.UpsertAsync(
                code: "rr_sem_m_sub",
                name: "RR Semantics M/Sub",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(registerId, [], CancellationToken.None);
            await mgmt.ReplaceFieldsAsync(registerId, [], CancellationToken.None);

            docId = await drafts.CreateDraftAsync(
                typeCode: "test_doc",
                number: "RR-SEM-2",
                dateUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                manageTransaction: true,
                ct: CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();

            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        // Assert: semantic null-check constraints are NOT present for this shape.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var recorderNullCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM pg_constraint
                WHERE conrelid = @Table::regclass
                  AND contype = 'c'
                  AND conname LIKE 'ck_refreg_recorder_null_%';
                """,
                new { Table = table });

            recorderNullCount.Should().Be(0, "SubordinateToRecorder registers must not have recorder_document_id IS NULL check");

            var nonPeriodicCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM pg_constraint
                WHERE conrelid = @Table::regclass
                  AND contype = 'c'
                  AND conname LIKE 'ck_refreg_nonperiodic_%';
                """,
                new { Table = table });

            nonPeriodicCount.Should().Be(0, "Periodic registers must not have the NonPeriodic NULL-periods check");
        }

        // DB-level enforcement: recorder_document_id is NOT NULL for SubordinateToRecorder.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var dt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"""
                    INSERT INTO {table} (dimension_set_id, period_utc, period_bucket_utc, recorder_document_id, recorded_at_utc, is_deleted)
                    VALUES (@DimSetId, @PeriodUtc, @BucketUtc, NULL, @RecordedAtUtc, FALSE);
                    """,
                    new
                    {
                        DimSetId = Guid.Empty,
                        PeriodUtc = dt,
                        BucketUtc = dt,
                        RecordedAtUtc = dt
                    });
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("23502");
        }

        // DB-level enforcement: period columns are NOT NULL for periodic registers.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    $"""
                    INSERT INTO {table} (dimension_set_id, period_utc, period_bucket_utc, recorder_document_id, recorded_at_utc, is_deleted)
                    VALUES (@DimSetId, NULL, NULL, @RecorderId, @RecordedAtUtc, FALSE);
                    """,
                    new
                    {
                        DimSetId = Guid.Empty,
                        RecorderId = docId,
                        RecordedAtUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)
                    });
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("23502");
        }
    }
}
