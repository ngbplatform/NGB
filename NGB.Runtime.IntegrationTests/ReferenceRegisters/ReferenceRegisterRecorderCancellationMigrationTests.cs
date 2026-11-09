using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations;
using NGB.PostgreSql.Migrations.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[CollectionDefinition(Name)]
public sealed class ReferenceRegisterUpgradeCollection : ICollectionFixture<ReferenceRegisterUpgradeFixture>
{
    public const string Name = "Reference register cancellation upgrade";
}

public sealed class ReferenceRegisterUpgradeFixture : PostgresTestFixture
{
    protected override bool RebuildSchemaBeforeReset => true;

    protected override async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Start with the actual prior migration history, without modifying applied checksums
        // or depending on a locally built legacy DLL. The test then runs the ordinary migrator.
        var assembly = typeof(DatabaseBootstrapper).Assembly;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var evolve = new EvolveDb.Evolve(connection)
        {
            IsEraseDisabled = true,
            Schemas = ["public"],
            MetadataTableName = "migration_changelog__platform",
            // Keep later platform migrations out of this historical starting point.
            TargetVersion = new EvolveDb.Migration.MigrationVersion("2026_11_08_0100"),
            EmbeddedResourceAssemblies = [assembly],
            EmbeddedResourceFilters = ["NGB.PostgreSql.db.migrations.V"]
        };
        evolve.Migrate();

        await new PostgresMigrationRunner(connectionString).RunAsync(
            DatabaseBootstrapper.BuildPlatformRepairDdlObjects()
                .Where(migration => migration is not ReferenceRegisterRecorderCancellationMigration)
                .ToArray(),
            ct: cancellationToken);
    }
}

[Collection(ReferenceRegisterUpgradeCollection.Name)]
public sealed class ReferenceRegisterRecorderCancellationMigrationTests(ReferenceRegisterUpgradeFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Column = "__is_recorder_cancellation";

    [Theory]
    [InlineData(ReferenceRegisterPeriodicity.Second, ReferenceRegisterRecordMode.SubordinateToRecorder, true)]
    [InlineData(ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.SubordinateToRecorder, true)]
    [InlineData(ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder, true)]
    [InlineData(ReferenceRegisterPeriodicity.Quarter, ReferenceRegisterRecordMode.SubordinateToRecorder, true)]
    [InlineData(ReferenceRegisterPeriodicity.Year, ReferenceRegisterRecordMode.SubordinateToRecorder, true)]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.SubordinateToRecorder, false)]
    [InlineData(ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.Independent, false)]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, false)]
    public async Task Upgrade_DiscoversApplicationRegisters_PreservesRowsAndPublicValues(
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode mode,
        bool expectsColumn)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var legacy = await CreateLegacyRegisterAsync(host, "unknown_application_register", periodicity, mode);
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        var before = await connection.QuerySingleAsync<string>($"SELECT row_to_json(r)::text FROM {legacy.Table} r;");

        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);
        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);

        (await HasColumnAsync(connection, legacy.Table)).Should().Be(expectsColumn);
        var after = await connection.QuerySingleAsync<string>($"""
            SELECT row_to_json(r)::text FROM (
                SELECT record_id, dimension_set_id, period_utc, period_bucket_utc,
                       recorder_document_id, recorded_at_utc, is_deleted, amount
                FROM {legacy.Table}
            ) r;
            """);
        after.Should().Be(before);
        if (expectsColumn)
            (await connection.QuerySingleAsync<bool>($"SELECT {Column} FROM {legacy.Table};")).Should().BeFalse();

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>().AppendAsync(legacy.RegisterId,
            [new ReferenceRegisterRecordWrite(Guid.Empty, legacy.Period, legacy.RecorderId,
                new Dictionary<string, object?> { ["amount"] = 130 })]);
        await uow.CommitAsync();
        var current = await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .SliceLastForEffectiveMomentAsync(legacy.RegisterId, Guid.Empty, DateTime.UtcNow.AddYears(1),
                DateTime.UtcNow.AddYears(1), legacy.RecorderId);
        current!.Values.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("amount", 130));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrade_RefusesExistingUnownedOrBusinessColumn_AndRollsBack(bool registeredBusinessField)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var legacy = await CreateLegacyRegisterAsync(host, "conflicting_application_register",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder,
            registeredBusinessField: registeredBusinessField);
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        if (!registeredBusinessField)
            await connection.ExecuteAsync($"ALTER TABLE {legacy.Table} ADD COLUMN {Column} boolean NOT NULL DEFAULT true;");
        var before = await connection.QuerySingleAsync<string>($"SELECT row_to_json(r)::text FROM {legacy.Table} r;");

        var upgrade = () => DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);
        await upgrade.Should().ThrowAsync<Exception>()
            .Where(error => error.ToString().Contains(Column));

        (await connection.QuerySingleAsync<string>($"SELECT row_to_json(r)::text FROM {legacy.Table} r;"))
            .Should().Be(before, "an incompatible application column must never be reused or overwritten");
    }

    [Fact]
    public async Task Upgrade_DoesNotGuessLegacyDeletionMeaning_AndExplicitReplacementCanRepairTheEffect()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var legacy = await CreateLegacyRegisterAsync(host, "legacy_cancellation_history",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"""
            INSERT INTO {legacy.Table} (dimension_set_id, period_utc, period_bucket_utc, recorder_document_id, is_deleted, amount)
            SELECT dimension_set_id, period_utc, period_bucket_utc, recorder_document_id, true, amount FROM {legacy.Table};
            """);

        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var original = await reader.ListByRecorderDocumentAsync(legacy.RegisterId, legacy.RecorderId!.Value);
        original.Should().HaveCount(2);
        original.First().IsDeleted.Should().BeTrue();
        (await connection.QuerySingleAsync<int>($"SELECT count(*)::integer FROM {legacy.Table} WHERE {Column};")).Should().Be(0);
        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await uow.BeginTransactionAsync();
        await ((IReferenceRegisterRecorderTombstoneWriter)store).AppendTombstonesForRecorderAsync(
            legacy.RegisterId, legacy.RecorderId.Value, null);
        await store.AppendAsync(legacy.RegisterId,
            [new ReferenceRegisterRecordWrite(Guid.Empty, legacy.Period!.Value.AddDays(-1), legacy.RecorderId,
                new Dictionary<string, object?> { ["amount"] = 130 })]);
        await uow.CommitAsync();
        var current = await reader.SliceLastAsync(legacy.RegisterId, Guid.Empty, DateTime.UtcNow.AddYears(1), legacy.RecorderId);
        current!.Values["amount"].Should().Be(130);
        (await reader.ListByRecorderDocumentAsync(legacy.RegisterId, legacy.RecorderId.Value))
            .Where(row => original.Any(old => old.RecordId == row.RecordId)).Should().BeEquivalentTo(original);
        var repaired = (await connection.QueryAsync<string>($"SELECT row_to_json(r)::text FROM {legacy.Table} r ORDER BY record_id;")).ToArray();

        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);
        await DatabaseBootstrapper.RepairAsync(Fixture.ConnectionString);

        (await connection.QueryAsync<string>($"SELECT row_to_json(r)::text FROM {legacy.Table} r ORDER BY record_id;"))
            .Should().Equal(repaired, "repeating migration/repair must retain cancellation flags and the entire history");
    }

    [Fact]
    public async Task NewSchema_SeparatesBusinessFieldName_DetectsDrift_AndRejectsLiveCancellationRows()
    {
        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await management.UpsertAsync("new_cancellation_schema", "New schema",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        await management.ReplaceFieldsAsync(registerId,
            [new ReferenceRegisterFieldDefinition(Column, "Business value", 1, ColumnType.Int32, false)]);
        var documentId = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test_doc", "CANCELLATION-SCHEMA", DateTime.UtcNow);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await uow.BeginTransactionAsync();
        await store.AppendAsync(registerId,
            [new ReferenceRegisterRecordWrite(Guid.Empty, DateTime.UtcNow, documentId,
                new Dictionary<string, object?> { [Column] = 130 })]);
        await uow.CommitAsync();
        var rows = await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .ListByRecorderDocumentAsync(registerId, documentId);
        rows.Single().Values.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>(Column, 130));
        const string table = "refreg_new_cancellation_schema__records";
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        var invalid = () => connection.ExecuteAsync($"""
            INSERT INTO {table} (dimension_set_id, period_utc, period_bucket_utc, recorder_document_id,
                                is_deleted, is_recorder_cancellation, {Column})
            SELECT dimension_set_id, period_utc, period_bucket_utc, recorder_document_id, false, 999, true FROM {table};
            """);
        (await invalid.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");

        await connection.ExecuteAsync($"ALTER TABLE {table} DROP COLUMN {Column};");
        var health = await scope.ServiceProvider.GetRequiredService<IReferenceRegisterPhysicalSchemaHealthReader>()
            .GetByRegisterIdAsync(registerId);
        health!.Records.MissingColumns.Should().Contain(Column);
        var validate = () => scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>().ValidateAsync();
        await validate.Should().ThrowAsync<NgbConfigurationViolationException>();

        await using var repairScope = host.Services.CreateAsyncScope();
        await repairScope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>().EnsureReadyForWriteAsync(registerId);
        await repairScope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>().ValidateAsync();
    }

    private static async Task<LegacyRegister> CreateLegacyRegisterAsync(
        IHost host,
        string code,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode mode,
        bool registeredBusinessField = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        Guid? recorderId = mode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
                .CreateDraftAsync("test_doc", code, DateTime.UtcNow)
            : null;
        var registerId = ReferenceRegisterId.FromCode(code);
        var table = ReferenceRegisterNaming.RecordsTable(code);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>()
            .UpsertAsync(new ReferenceRegisterUpsert(registerId, code, code, periodicity, mode), DateTime.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IReferenceRegisterFieldRepository>().ReplaceAsync(registerId,
            [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Int32, false)], DateTime.UtcNow);
        var businessColumn = registeredBusinessField ? Column : "amount";
        if (registeredBusinessField)
        {
            await uow.Connection.ExecuteAsync(
                "UPDATE reference_register_fields SET column_code = @Column WHERE register_id = @Id;",
                new { Column, Id = registerId }, uow.Transaction);
        }

        // This is the released physical layout before cancellation metadata was introduced.
        await uow.Connection.ExecuteAsync($"""
            CREATE TABLE {table} (
                record_id bigserial PRIMARY KEY,
                dimension_set_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
                    REFERENCES platform_dimension_sets(dimension_set_id),
                period_utc timestamptz,
                period_bucket_utc timestamptz,
                recorder_document_id uuid REFERENCES documents(id),
                recorded_at_utc timestamptz NOT NULL DEFAULT now(),
                is_deleted boolean NOT NULL DEFAULT false,
                {businessColumn} integer NOT NULL
            );
            """, transaction: uow.Transaction);
        DateTime? period = periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : DateTime.UtcNow.Date.AddMonths(1);
        await uow.Connection.ExecuteAsync($"""
            INSERT INTO {table} (period_utc, period_bucket_utc, recorder_document_id, {businessColumn})
            VALUES (@Period, @Bucket, @RecorderId, 100);
            UPDATE reference_registers SET has_records = true WHERE register_id = @RegisterId;
            """, new
        {
            Period = period,
            Bucket = ReferenceRegisterPeriodBucket.ComputeUtc(period, periodicity),
            RecorderId = recorderId,
            RegisterId = registerId
        }, uow.Transaction);
        await uow.CommitAsync();
        return new LegacyRegister(registerId, table, recorderId, period);
    }

    private static Task<bool> HasColumnAsync(NpgsqlConnection connection, string table)
        => connection.QuerySingleAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @Table AND column_name = @Column
            );
            """, new { Table = table, Column });

    private sealed record LegacyRegister(Guid RegisterId, string Table, Guid? RecorderId, DateTime? Period);
}
