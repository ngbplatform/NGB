using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: EnsureSchemaAsync for per-register Operational Registers tables can repair common drift:
/// - missing append-only guard on movements
/// - missing movement indexes
/// - missing derived tables (turnovers/balances)
/// - missing resource columns on derived tables
///
/// We intentionally focus on drift patterns that EnsureSchema is expected to fix.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterPhysicalSchema_EnsureSchema_DriftRepair_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Movements_AppendOnlyGuardDropped_EnsureSchema_RecreatesTrigger_AndBlocksMutation()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        // Break: drop the append-only trigger(s).
        await DropAppendOnlyGuardTriggersAsync(Fixture.ConnectionString, movementsTable);
        (await CountAppendOnlyGuardTriggersAsync(Fixture.ConnectionString, movementsTable)).Should().Be(0);

        // Repair: EnsureSchema must recreate the trigger.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        (await CountAppendOnlyGuardTriggersAsync(Fixture.ConnectionString, movementsTable)).Should().Be(1);

        // Assert: mutation is forbidden again.
        await InsertMovementRowAsync(Fixture.ConnectionString, movementsTable);
        await AssertUpdateForbiddenAsync(Fixture.ConnectionString, movementsTable);

        // And health is OK.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterPhysicalSchemaHealthReader>();
            var health = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.Movements.HasAppendOnlyGuard.Should().BeTrue();
            health.Movements.IsOk.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Movements_DocIndexDropped_EnsureSchema_RecreatesIndex()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        // Find the "doc only" index by definition.
        var indexName = await FindDocOnlyIndexNameAsync(Fixture.ConnectionString, movementsTable);
        indexName.Should().NotBeNullOrWhiteSpace();

        // Break.
        await DropIndexAsync(Fixture.ConnectionString, indexName!);
        (await CountDocOnlyIndexesAsync(Fixture.ConnectionString, movementsTable)).Should().Be(0);

        // Repair.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        (await CountDocOnlyIndexesAsync(Fixture.ConnectionString, movementsTable)).Should().Be(1);
    }

    [Fact]
    public async Task DerivedTables_Dropped_EnsureSchema_RecreatesTables_AndUniqueConstraints()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);

        var turnoversTable = OperationalRegisterNaming.TurnoversTable(tableCode);
        var balancesTable = OperationalRegisterNaming.BalancesTable(tableCode);

        // Break: drop tables entirely (most common drift in dev DBs).
        await DropTableAsync(Fixture.ConnectionString, turnoversTable);
        await DropTableAsync(Fixture.ConnectionString, balancesTable);

        (await TableExistsAsync(Fixture.ConnectionString, turnoversTable)).Should().BeFalse();
        (await TableExistsAsync(Fixture.ConnectionString, balancesTable)).Should().BeFalse();

        // Repair.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await turnovers.EnsureSchemaAsync(registerId, CancellationToken.None);
            await balances.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        // Tables exist again and have the UNIQUE(period_month, dimension_set_id) contract.
        (await TableExistsAsync(Fixture.ConnectionString, turnoversTable)).Should().BeTrue();
        (await TableExistsAsync(Fixture.ConnectionString, balancesTable)).Should().BeTrue();

        (await CountUniqueConstraintsAsync(Fixture.ConnectionString, turnoversTable)).Should().BeGreaterThan(0);
        (await CountUniqueConstraintsAsync(Fixture.ConnectionString, balancesTable)).Should().BeGreaterThan(0);

        // Health should be OK.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterPhysicalSchemaHealthReader>();
            var health = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.Turnovers.IsOk.Should().BeTrue();
            health.Balances.IsOk.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DerivedTables_ResourceColumnDropped_EnsureSchema_RecreatesColumns()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);

        var turnoversTable = OperationalRegisterNaming.TurnoversTable(tableCode);
        var balancesTable = OperationalRegisterNaming.BalancesTable(tableCode);

        // Break: drop one resource column.
        await DropColumnAsync(Fixture.ConnectionString, turnoversTable, "amount");
        await DropColumnAsync(Fixture.ConnectionString, balancesTable, "amount");

        (await ColumnExistsAsync(Fixture.ConnectionString, turnoversTable, "amount")).Should().BeFalse();
        (await ColumnExistsAsync(Fixture.ConnectionString, balancesTable, "amount")).Should().BeFalse();

        // Repair.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await turnovers.EnsureSchemaAsync(registerId, CancellationToken.None);
            await balances.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        (await ColumnExistsAsync(Fixture.ConnectionString, turnoversTable, "amount")).Should().BeTrue();
        (await ColumnExistsAsync(Fixture.ConnectionString, balancesTable, "amount")).Should().BeTrue();

        // Health should be OK.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterPhysicalSchemaHealthReader>();
            var health = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.Turnovers.MissingColumns.Should().BeEmpty();
            health.Balances.MissingColumns.Should().BeEmpty();
            health.Turnovers.IsOk.Should().BeTrue();
            health.Balances.IsOk.Should().BeTrue();
        }
    }

    private static async Task<(Guid RegisterId, string TableCode)> CreateRegisterAndEnsureSchemaAsync(
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await mgmt.UpsertAsync("RR", "Rent Roll", ct);

        await mgmt.ReplaceResourcesAsync(
            registerId,
            [
                new OperationalRegisterResourceDefinition("Amount", "Amount", 10)
            ],
            ct);

        var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        await movements.EnsureSchemaAsync(registerId, ct);
        await turnovers.EnsureSchemaAsync(registerId, ct);
        await balances.EnsureSchemaAsync(registerId, ct);

        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, ct);
        reg.Should().NotBeNull();

        return (registerId, reg!.TableCode);
    }

    private static async Task DropAppendOnlyGuardTriggersAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        await ExecuteNonQueryAsync(
            cs,
            $"""
            DO $$
            DECLARE r record;
            BEGIN
                FOR r IN
                    SELECT t.tgname
                    FROM pg_trigger t
                    JOIN pg_proc p ON p.oid = t.tgfoid
                    WHERE t.tgrelid = to_regclass('{table}')
                      AND NOT t.tgisinternal
                      AND p.proname = 'ngb_forbid_mutation_of_append_only_table'
                LOOP
                    EXECUTE format('DROP TRIGGER %I ON %I', r.tgname, '{table}');
                END LOOP;
            END $$;
            """);
    }

    private static async Task<int> CountAppendOnlyGuardTriggersAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        return await ExecuteScalarIntAsync(
            cs,
            $"""
            SELECT COUNT(*)
            FROM pg_trigger t
            JOIN pg_proc p ON p.oid = t.tgfoid
            WHERE t.tgrelid = to_regclass('{table}')
              AND NOT t.tgisinternal
              AND p.proname = 'ngb_forbid_mutation_of_append_only_table';
            """);
    }

    private static async Task InsertMovementRowAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        await ExecuteNonQueryAsync(
            cs,
            $"INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, is_storno) VALUES ('{Guid.CreateVersion7()}', NOW(), '{Guid.Empty}', FALSE);");
    }

    private static async Task AssertUpdateForbiddenAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        var act = async () =>
        {
            await ExecuteNonQueryAsync(cs, $"UPDATE {table} SET is_storno = is_storno;");
        };

        var ex = await Assert.ThrowsAsync<PostgresException>(act);
        ex.SqlState.Should().BeOneOf("55000", "P0001");
    }

    private static async Task<string?> FindDocOnlyIndexNameAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = @Table
              AND indexdef LIKE '%(document_id)%'
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("Table", table);

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task<int> CountDocOnlyIndexesAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        return await ExecuteScalarIntAsync(
            cs,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = @Table
              AND indexdef LIKE '%(document_id)%';
            """,
            p => p.Parameters.AddWithValue("Table", table));
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        EnsureIdentifier(indexName);
        await ExecuteNonQueryAsync(cs, $"DROP INDEX IF EXISTS {indexName};");
    }

    private static async Task DropTableAsync(string cs, string table)
    {
        EnsureIdentifier(table);
        await ExecuteNonQueryAsync(cs, $"DROP TABLE IF EXISTS {table};");
    }

    private static async Task<bool> TableExistsAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        var count = await ExecuteScalarIntAsync(
            cs,
            """
            SELECT CASE WHEN to_regclass(@Table) IS NULL THEN 0 ELSE 1 END;
            """,
            p => p.Parameters.AddWithValue("Table", table));

        return count == 1;
    }

    private static async Task<int> CountUniqueConstraintsAsync(string cs, string table)
    {
        EnsureIdentifier(table);

        return await ExecuteScalarIntAsync(
            cs,
            """
            SELECT COUNT(*)
            FROM pg_constraint
            WHERE conrelid = to_regclass(@Table)
              AND contype = 'u';
            """,
            p => p.Parameters.AddWithValue("Table", table));
    }

    private static async Task DropColumnAsync(string cs, string table, string column)
    {
        EnsureIdentifier(table);
        EnsureIdentifier(column);

        await ExecuteNonQueryAsync(cs, $"ALTER TABLE {table} DROP COLUMN {column};");
    }

    private static async Task<bool> ColumnExistsAsync(string cs, string table, string column)
    {
        EnsureIdentifier(table);
        EnsureIdentifier(column);

        var count = await ExecuteScalarIntAsync(
            cs,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @Table
              AND column_name = @Column;
            """,
            p =>
            {
                p.Parameters.AddWithValue("Table", table);
                p.Parameters.AddWithValue("Column", column);
            });

        return count > 0;
    }

    private static async Task ExecuteNonQueryAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteScalarIntAsync(string cs, string sql, Action<NpgsqlCommand>? configure = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        var obj = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(obj);
    }

    private static void EnsureIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new NgbArgumentRequiredException(nameof(identifier));

        if (!Regex.IsMatch(identifier, "^[a-z0-9_]+$"))
            throw new NgbArgumentInvalidException(nameof(identifier), $"Unsafe SQL identifier '{identifier}'.");
    }
}
