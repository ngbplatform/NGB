using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P0: Core schema validator must validate per-register physical __records tables
/// for reference registers that already have records (has_records=true).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegistersCoreSchemaValidation_PhysicalRecordsTables_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenPhysicalTableAppendOnlyTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (_, table) = await CreateRegisterAndAppendOneRecordAsync(
            host,
            code: "rr_schema_phys_trg",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        // Drop the append-only trigger on the physical table.
        await DropAppendOnlyTriggerAsync(Fixture.ConnectionString, table);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage($"*{table}*append-only trigger*");
    }

    [Fact]
    public async Task ValidateAsync_WhenPhysicalTableKeyV2IndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (_, table) = await CreateRegisterAndAppendOneRecordAsync(
            host,
            code: "rr_schema_phys_ix",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        // Drop the v2 key index (name is hashed, but prefix is stable).
        await DropIndexByPrefixAsync(Fixture.ConnectionString, table, "ix_refreg_key_v2_");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage($"*{table}*key_v2 index*");
    }

    private static async Task<(Guid RegisterId, string Table)> CreateRegisterAndAppendOneRecordAsync(
        IHost host,
        string code,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        CancellationToken ct)
    {
        Guid registerId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "schema phys",
                periodicity,
                recordMode,
                ct);

            await mgmt.ReplaceDimensionRulesAsync(registerId, [], ct);
            await mgmt.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        "v",
                        "V",
                        10,
                        ColumnType.Int32,
                        true)
                ],
                ct);

            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await store.AppendAsync(
                    registerId,
                    records:
                    [
                        new ReferenceRegisterRecordWrite(
                            DimensionSetId: Guid.Empty,
                            PeriodUtc: periodicity == ReferenceRegisterPeriodicity.NonPeriodic
                                ? null
                                : new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                            RecorderDocumentId: null,
                            Values: new Dictionary<string, object?>
                            {
                                ["v"] = 1
                            },
                            IsDeleted: false)
                    ],
                    innerCt);
            }, ct);

            var reg = await repo.GetByIdAsync(registerId, ct);
            reg.Should().NotBeNull();

            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        return (registerId, table);
    }

    private static async Task DropAppendOnlyTriggerAsync(string cs, string table)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var trgName = await conn.QuerySingleOrDefaultAsync<string>(
            """
            SELECT t.tgname
            FROM pg_trigger t
            JOIN pg_class cl ON cl.oid = t.tgrelid
            JOIN pg_namespace ns ON ns.oid = cl.relnamespace
            JOIN pg_proc p ON p.oid = t.tgfoid
            WHERE ns.nspname = 'public'
              AND cl.relname = @Table
              AND NOT t.tgisinternal
              AND p.proname = 'ngb_forbid_mutation_of_append_only_table'
            LIMIT 1;
            """,
            new { Table = table });

        trgName.Should().NotBeNullOrWhiteSpace();

        await conn.ExecuteAsync($"DROP TRIGGER IF EXISTS {trgName} ON {table};");
    }

    private static async Task DropIndexByPrefixAsync(string cs, string table, string indexPrefix)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var indexName = await conn.QuerySingleOrDefaultAsync<string>(
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = @Table
              AND indexname LIKE @Prefix;
            """,
            new { Table = table, Prefix = indexPrefix + "%" });

        indexName.Should().NotBeNullOrWhiteSpace();

        await conn.ExecuteAsync($"DROP INDEX IF EXISTS {indexName};");
    }
}
