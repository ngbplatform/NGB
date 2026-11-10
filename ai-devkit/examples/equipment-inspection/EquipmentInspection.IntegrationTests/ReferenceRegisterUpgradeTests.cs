using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReferenceRegisterUpgradeTests
{
    private const string RegisterCode = "example.legacy-rates";
    private const string CancellationColumn = "__is_recorder_cancellation";
    private static readonly DateTime OriginalDate = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EarlierDate = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ReferenceRegisterPeriodicity.Month, false)]
    [InlineData(ReferenceRegisterPeriodicity.Day, false)]
    [InlineData(ReferenceRegisterPeriodicity.Month, true)]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic, false)]
    public async Task Migrator_preserves_legacy_rows_and_new_cancellations_do_not_hide_a_backdated_replacement(
        ReferenceRegisterPeriodicity periodicity, bool legacyDeletion)
    {
        var registerId = ReferenceRegisterId.FromCode(RegisterCode);
        var table = ReferenceRegisterNaming.RecordsTable(RegisterCode);
        var recorder = Guid.CreateVersion7();
        string? originalJson = null;
        await using var host = new InspectionHost(prepareDatabase: async connectionString =>
        {
            await CreateLegacySchemaAsync(connectionString, registerId, table, recorder, periodicity, legacyDeletion);
            await using var connection = new NpgsqlConnection(connectionString);
            Assert.False(await HasCancellationColumnAsync(connection, table));
            originalJson = await connection.QuerySingleAsync<string>($"SELECT row_to_json(r)::text FROM {table} r;");
        });
        // InspectionHost invokes the actual application Migrator twice before starting the API.
        await host.StartAsync();
        await using var verification = new NpgsqlConnection(host.ConnectionString);
        var periodic = periodicity != ReferenceRegisterPeriodicity.NonPeriodic;
        Assert.Equal(periodic, await HasCancellationColumnAsync(verification, table));
        Assert.Equal(originalJson, await verification.QuerySingleAsync<string>($"""
            SELECT row_to_json(r)::text FROM (
                SELECT record_id, dimension_set_id, period_utc, period_bucket_utc,
                       recorder_document_id, recorded_at_utc, is_deleted, amount
                FROM {table}
            ) r;
            """));
        if (periodic)
        {
            Assert.False(await verification.QuerySingleAsync<bool>($"SELECT {CancellationColumn} FROM {table};"));
        }

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var records = services.GetRequiredService<IReferenceRegisterRecordsReader>();
        var store = services.GetRequiredService<IReferenceRegisterRecordsStore>();
        var cancellations = Assert.IsAssignableFrom<IReferenceRegisterRecorderTombstoneWriter>(store);
        var uow = services.GetRequiredService<IUnitOfWork>();
        var original = Assert.Single(await records.ListByRecorderDocumentAsync(registerId, recorder));
        Assert.Equal(legacyDeletion, original.IsDeleted);
        Assert.Equal(1000m, original.Values["amount"]);
        Assert.Single(original.Values);
        var current = await records.SliceLastAsync(registerId, Guid.Empty, DateTime.UtcNow, recorder);
        Assert.Equal(legacyDeletion, current!.IsDeleted);

        foreach (var (date, amount) in new[] { (OriginalDate, 900m), (EarlierDate, 1400m) })
        {
            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await cancellations.AppendTombstonesForRecorderAsync(registerId, recorder, null, ct);
            });
            var cancelled = await records.SliceLastAsync(registerId, Guid.Empty, DateTime.UtcNow, recorder);
            Assert.True(cancelled is null || cancelled.IsDeleted);
            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await store.AppendAsync(registerId,
                    [new ReferenceRegisterRecordWrite(Guid.Empty, periodic ? date : null, recorder,
                        new Dictionary<string, object?> { ["amount"] = amount })], ct);
            });
            var active = await records.SliceLastAsync(registerId, Guid.Empty, DateTime.UtcNow, recorder);
            Assert.NotNull(active);
            Assert.Equal(amount, active.Values["amount"]);
            Assert.Single(active.Values);
            if (periodic)
            {
                foreach (var effective in new[] { date, OriginalDate, OriginalDate.AddMonths(1) })
                {
                    var slice = await records.SliceLastForEffectiveMomentAsync(registerId, Guid.Empty,
                        effective, DateTime.UtcNow, recorder);
                    Assert.Equal(active.RecordId, slice!.RecordId);
                }
                var beforeEffectiveDate = await records.SliceLastForEffectiveMomentAsync(registerId, Guid.Empty,
                    date.AddTicks(-10), DateTime.UtcNow, recorder);
                Assert.True(beforeEffectiveDate is null || beforeEffectiveDate.IsDeleted);
            }
        }

        var history = await records.ListByRecorderDocumentAsync(registerId, recorder);
        var retained = Assert.Single(history, row => row.RecordId == original.RecordId);
        Assert.Equal(original.RecordedAtUtc, retained.RecordedAtUtc);
        Assert.Equal(original.PeriodUtc, retained.PeriodUtc);
        Assert.Equal(original.IsDeleted, retained.IsDeleted);
        Assert.Equal(original.Values["amount"], retained.Values["amount"]);
        var asOriginallyKnown = periodic
            ? await records.SliceLastForEffectiveMomentAsync(registerId, Guid.Empty, OriginalDate, original.RecordedAtUtc, recorder)
            : await records.SliceLastAsync(registerId, Guid.Empty, original.RecordedAtUtc, recorder);
        if (legacyDeletion)
        {
            Assert.True(asOriginallyKnown!.IsDeleted);
        }
        else
        {
            Assert.Equal(original.RecordId, asOriginallyKnown!.RecordId);
        }

        var beforeRepeat = (await verification.QueryAsync<string>($"SELECT row_to_json(r)::text FROM {table} r ORDER BY record_id;")).ToArray();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + host.ConnectionString]));
        Assert.Equal(beforeRepeat, await verification.QueryAsync<string>($"SELECT row_to_json(r)::text FROM {table} r ORDER BY record_id;"));
    }

    private static async Task CreateLegacySchemaAsync(string connectionString, Guid registerId, string table,
        Guid recorder, ReferenceRegisterPeriodicity periodicity, bool legacyDeletion)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // Replay the packaged migration prefix, retaining real checksums. Do not fake
        // an applied migration or enable OutOfOrder to manufacture an upgrade result.
        var evolve = new EvolveDb.Evolve(connection)
        {
            IsEraseDisabled = true,
            Schemas = ["public"],
            MetadataTableName = "migration_changelog__platform",
            TargetVersion = new EvolveDb.Migration.MigrationVersion("2026_11_08_0100"),
            EmbeddedResourceAssemblies = [typeof(DatabaseBootstrapper).Assembly],
            EmbeddedResourceFilters = ["NGB.PostgreSql.db.migrations.V"]
        };
        evolve.Migrate();

        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddNgbRuntime().AddNgbPostgres(connectionString);
        await using var provider = registrations.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var uow = services.GetRequiredService<IUnitOfWork>();
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await services.GetRequiredService<IReferenceRegisterRepository>().UpsertAsync(
                new ReferenceRegisterUpsert(registerId, RegisterCode, "Legacy application rates", periodicity,
                    ReferenceRegisterRecordMode.SubordinateToRecorder), DateTime.UtcNow, ct);
            await services.GetRequiredService<IReferenceRegisterFieldRepository>().ReplaceAsync(registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Decimal, false)], DateTime.UtcNow, ct);
            // Fixture-only legacy layout and history, belonging to an application unknown to the migrator.
            await uow.Connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO documents (id, type_code, date_utc, status)
                VALUES (@Recorder, 'example.legacy-rate-document', @Date, 1);
                CREATE TABLE {table} (
                    record_id bigserial PRIMARY KEY,
                    dimension_set_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
                        REFERENCES platform_dimension_sets(dimension_set_id),
                    period_utc timestamptz,
                    period_bucket_utc timestamptz,
                    recorder_document_id uuid REFERENCES documents(id),
                    recorded_at_utc timestamptz NOT NULL DEFAULT now(),
                    is_deleted boolean NOT NULL DEFAULT false,
                    amount numeric(28, 8) NOT NULL
                );
                INSERT INTO {table} (period_utc, period_bucket_utc, recorder_document_id, is_deleted, amount)
                VALUES (@Period, @Bucket, @Recorder, @Deleted, 1000);
                UPDATE reference_registers SET has_records = true WHERE register_id = @Register;
                """, new
            {
                Recorder = recorder,
                Date = OriginalDate,
                Period = periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? (DateTime?)null : OriginalDate,
                Bucket = ReferenceRegisterPeriodBucket.ComputeUtc(
                    periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : OriginalDate, periodicity),
                Deleted = legacyDeletion,
                Register = registerId
            }, transaction: uow.Transaction, cancellationToken: ct));
        });
    }

    private static Task<bool> HasCancellationColumnAsync(NpgsqlConnection connection, string table)
        => connection.QuerySingleAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @Table AND column_name = @Column
            );
            """, new { Table = table, Column = CancellationColumn });
}
