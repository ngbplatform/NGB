using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// DB-level guards for Reference Registers metadata.
///
/// These tests deliberately bypass Runtime services and mutate metadata tables directly,
/// asserting that PostgreSQL triggers enforce production invariants when has_records=true.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_ReferenceRegisters_MetadataGuards_WhenHasRecords_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Registers_AreImmutableAfterHasRecords_And_HasRecordsIsMonotonic()
    {
        var registerId = await SeedRegisterWithOneRecordAsync(
            code: "it_refreg_guard_reg",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            includeField: false,
            includeDimensionRule: false);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Name may change (allowed)
        await conn.ExecuteAsync(
            "UPDATE reference_registers SET name = @Name WHERE register_id = @Id;",
            new { Id = registerId, Name = "RR Guard Renamed" });

        var name = await conn.ExecuteScalarAsync<string?>(
            "SELECT name FROM reference_registers WHERE register_id = @Id;",
            new { Id = registerId });

        name.Should().Be("RR Guard Renamed");

        // Code/periodicity/record_mode are immutable once has_records=true.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_registers SET code = @Code WHERE register_id = @Id;",
            new { Id = registerId, Code = "it_refreg_guard_reg_new" }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_registers SET periodicity = @P WHERE register_id = @Id;",
            new { Id = registerId, P = (short)ReferenceRegisterPeriodicity.Month }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_registers SET record_mode = @M WHERE register_id = @Id;",
            new { Id = registerId, M = (short)ReferenceRegisterRecordMode.SubordinateToRecorder }));

        // has_records can never flip back.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_registers SET has_records = FALSE WHERE register_id = @Id;",
            new { Id = registerId }));

        // Delete is forbidden after records exist.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "DELETE FROM reference_registers WHERE register_id = @Id;",
            new { Id = registerId }));
    }

    [Fact]
    public async Task Fields_AreImmutableAfterHasRecords_But_NameAndOrdinalMayChange()
    {
        var registerId = await SeedRegisterWithOneRecordAsync(
            code: "it_refreg_guard_fields",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            includeField: true,
            includeDimensionRule: false);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Name/ordinal are allowed.
        await conn.ExecuteAsync(
            "UPDATE reference_register_fields SET name = @Name, ordinal = @Ord WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", Name = "Value Renamed", Ord = 20 });

        var row = await conn.QuerySingleAsync<(string Name, int Ordinal)>(
            "SELECT name AS Name, ordinal AS Ordinal FROM reference_register_fields WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value" });

        row.Name.Should().Be("Value Renamed");
        row.Ordinal.Should().Be(20);

        // Identifiers are immutable after records exist.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_fields SET code = @Code WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", Code = "value2" }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_fields SET code_norm = @CodeNorm2 WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", CodeNorm2 = "value2" }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_fields SET column_code = @ColumnCode WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", ColumnCode = "value2" }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_fields SET column_type = @T WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", T = (short)ColumnType.Int32 }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_fields SET is_nullable = @N WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value", N = false }));

        // Delete is forbidden.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "DELETE FROM reference_register_fields WHERE register_id = @Id AND code_norm = @CodeNorm;",
            new { Id = registerId, CodeNorm = "value" }));
    }

    [Fact]
    public async Task DimensionRules_AreAppendOnlyAfterHasRecords_But_OptionalMayBeAdded()
    {
        var dimA = (Id: DeterministicGuid.Create("Dimension|it_refreg_dim_a"), Code: "it_refreg_dim_a");

        var registerId = await SeedRegisterWithOneRecordAsync(
            code: "it_refreg_guard_dim_rules",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            includeField: false,
            includeDimensionRule: true,
            dimRule: new ReferenceRegisterDimensionRule(dimA.Id, dimA.Code, Ordinal: 10, IsRequired: true));

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // DELETE / UPDATE are forbidden after has_records=true.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "DELETE FROM reference_register_dimension_rules WHERE register_id = @R AND dimension_id = @D;",
            new { R = registerId, D = dimA.Id }));

        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "UPDATE reference_register_dimension_rules SET ordinal = @O WHERE register_id = @R AND dimension_id = @D;",
            new { R = registerId, D = dimA.Id, O = 99 }));

        // Prepare new dimensions (to avoid FK failures and ensure the trigger is the reason for rejection).
        var dimB = (Id: DeterministicGuid.Create("Dimension|it_refreg_dim_b"), Code: "it_refreg_dim_b");
        var dimC = (Id: DeterministicGuid.Create("Dimension|it_refreg_dim_c"), Code: "it_refreg_dim_c");

        await EnsureDimensionExistsAsync(conn, dimB.Id, dimB.Code);
        await EnsureDimensionExistsAsync(conn, dimC.Id, dimC.Code);

        // Adding REQUIRED rule after records exist is forbidden.
        await AssertForbiddenAsync(conn, () => conn.ExecuteAsync(
            "INSERT INTO reference_register_dimension_rules(register_id, dimension_id, ordinal, is_required) VALUES (@R, @D, @O, TRUE);",
            new { R = registerId, D = dimC.Id, O = 30 }));

        // Adding OPTIONAL rule is allowed.
        await conn.ExecuteAsync(
            "INSERT INTO reference_register_dimension_rules(register_id, dimension_id, ordinal, is_required) VALUES (@R, @D, @O, FALSE);",
            new { R = registerId, D = dimB.Id, O = 20 });

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reference_register_dimension_rules WHERE register_id = @R;",
            new { R = registerId });

        count.Should().Be(2);
    }

    private async Task<Guid> SeedRegisterWithOneRecordAsync(
        string code,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        bool includeField,
        bool includeDimensionRule,
        ReferenceRegisterDimensionRule? dimRule = null)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registers = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var fieldsRepo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterFieldRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterDimensionRuleRepository>();
        var recordsStore = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        var maintenance = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminMaintenanceService>();

        var registerId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        // Create metadata (in a tx).
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await registers.UpsertAsync(
                new ReferenceRegisterUpsert(
                    RegisterId: registerId,
                    Code: code,
                    Name: code,
                    Periodicity: periodicity,
                    RecordMode: recordMode),
                nowUtc,
                ct);

            if (includeField)
            {
                await fieldsRepo.ReplaceAsync(
                    registerId,
                    [
                        new ReferenceRegisterFieldDefinition(
                            Code: "value",
                            Name: "Value",
                            Ordinal: 10,
                            ColumnType: ColumnType.String,
                            IsNullable: true)
                    ],
                    nowUtc,
                    ct);
            }

            if (includeDimensionRule)
            {
                dimRule ??= new ReferenceRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create("Dimension|it_refreg_dim_default"),
                    DimensionCode: "it_refreg_dim_default",
                    Ordinal: 10,
                    IsRequired: true);

                await rulesRepo.ReplaceAsync(registerId, [dimRule], nowUtc, ct);
            }
        }, CancellationToken.None);

        // Ensure physical table exists (non-transactional DDL is OK here).
        await maintenance.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);

        // Append a record (sets has_records=true in the same tx).
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var values = includeField
                ? new Dictionary<string, object?> { ["value"] = "v1" }
                : new Dictionary<string, object?>();

            await recordsStore.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: Guid.Empty,
                        PeriodUtc: periodicity == ReferenceRegisterPeriodicity.NonPeriodic
                            ? null
                            : nowUtc,
                        RecorderDocumentId: recordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
                            ? Guid.CreateVersion7()
                            : null,
                        Values: values,
                        IsDeleted: false)
                ],
                ct);
        }, CancellationToken.None);

        // Sanity: has_records must be true.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            var has = await conn.ExecuteScalarAsync<bool>(
                "SELECT has_records FROM reference_registers WHERE register_id = @Id;",
                new { Id = registerId });
            has.Should().BeTrue();
        }

        return registerId;
    }

    private static async Task EnsureDimensionExistsAsync(NpgsqlConnection conn, Guid dimensionId, string code)
    {
        var now = DateTime.UtcNow;

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(dimension_id, code, name, is_active, is_deleted, created_at_utc, updated_at_utc)
            VALUES (@Id, @Code, @Name, TRUE, FALSE, @Now, @Now)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { Id = dimensionId, Code = code, Name = code, Now = now });
    }

    private static async Task AssertForbiddenAsync(NpgsqlConnection conn, Func<Task> act)
    {
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().BeOneOf("55000", "P0001");
    }
}
