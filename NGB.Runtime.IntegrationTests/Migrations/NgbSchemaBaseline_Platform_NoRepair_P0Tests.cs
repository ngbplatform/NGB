using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.Schema;
using NGB.PostgreSql.Bootstrap;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Migrations;

/// <summary>
/// P0: The Evolve baseline must be sufficient to build a clean database from scratch.
/// </summary>
[Collection(SchemaPostgresCollection.Name)]
public sealed class NgbSchemaBaseline_Platform_NoRepair_P0Tests(SchemaPostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_FromEmptyOrReleasedSchema_PreservesData_AndValidatorsPass(bool upgradeFromRelease)
    {
        var dbName = "ngb_baseline_" + Guid.NewGuid().ToString("N")[..16];

        var csb = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = dbName,
            Pooling = false
        };

        var adminCsb = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await CreateDatabaseAsync(adminCsb.ConnectionString, dbName);

        try
        {
            var existingCatalogId = Guid.NewGuid();
            string[] releasedChecksums = [];
            if (upgradeFromRelease)
            {
                await using var released = new NpgsqlConnection(csb.ConnectionString);
                await released.OpenAsync();
                // These five scripts are the released platform migration set from main.
                // Use Evolve metadata so the subsequent upgrade must validate their checksums.
                var releasedMigrator = new EvolveDb.Evolve(released)
                {
                    IsEraseDisabled = true,
                    EnableClusterMode = false,
                    Schemas = ["public"],
                    MetadataTableName = "migration_changelog__platform",
                    EmbeddedResourceAssemblies = [typeof(DatabaseBootstrapper).Assembly],
                    EmbeddedResourceFilters = new[]
                    {
                        "V2026_02_20_0001__ngb_platform_baseline.sql",
                        "V2026_05_14_0100__ngb_platform_document_read_path_indexes.sql",
                        "V2026_06_10_0100__ngb_platform_security_rbac.sql",
                        "V2026_06_12_0100__platform_users_normalized_email_index.sql",
                        "V2026_07_26_0100__ngb_platform_document_actions_work_center.sql"
                    }.Select(name => "NGB.PostgreSql.db.migrations." + name).ToArray()
                };
                releasedMigrator.Migrate();
                releasedChecksums = (await released.QueryAsync<string>(
                    "SELECT version || ':' || checksum FROM migration_changelog__platform WHERE checksum IS NOT NULL;")).ToArray();
                releasedChecksums.Should().NotBeEmpty();
                (await released.ExecuteScalarAsync<bool>(
                    "SELECT to_regclass('public.ix_documents_type_posted_id') IS NOT NULL;")).Should().BeFalse();
                await released.ExecuteAsync(
                    "INSERT INTO catalogs(id, catalog_code) VALUES (@Id, 'preserved-catalog');",
                    new { Id = existingCatalogId });
            }

            // Act: Evolve-only initialization.
            await DatabaseBootstrapper.InitializeAsync(csb.ConnectionString);
            await DatabaseBootstrapper.InitializeAsync(csb.ConnectionString);

            var requiredIndexes = new[]
            {
                "ix_documents_type_posted_id",
                "ix_documents_type_active_updated_id",
                "ix_refreg_write_state_completed_document_operation_register",
                "ix_platform_users_display_sort",
                "ix_opreg_finalizations_dirty_queue",
                "ix_opreg_finalizations_blocked_queue",
                "ix_documents_number_trgm",
                "ix_accounting_accounts_code_trgm",
                "ix_accounting_accounts_name_trgm",
                "ix_doc_gje_reason_code_trgm",
                "ix_doc_gje_memo_trgm",
                "ix_doc_gje_external_reference_trgm"
            };

            // Assert: a few critical core tables exist.
            await using (var conn = new NpgsqlConnection(csb.ConnectionString))
            {
                await conn.OpenAsync();

                if (upgradeFromRelease)
                {
                    (await conn.QueryAsync<string>(
                        "SELECT version || ':' || checksum FROM migration_changelog__platform WHERE checksum IS NOT NULL;"))
                        .Should().Contain(releasedChecksums, "released migration checksums must survive the forward upgrade unchanged");
                    (await conn.ExecuteScalarAsync<string>(
                        "SELECT catalog_code FROM catalogs WHERE id = @Id;", new { Id = existingCatalogId }))
                        .Should().Be("preserved-catalog");
                }

                var criticalTables = new[]
                {
                    "platform_dimensions",
                    "platform_dimension_sets",
                    "platform_dimension_set_items",
                    "documents",
                    "catalogs",
                    "accounting_accounts",
                    "accounting_register_main",
                    "accounting_turnovers",
                    "accounting_balances",
                    "platform_audit_events",
                    "platform_audit_event_changes",
                    "operational_registers",
                    "reference_registers",
                };

                foreach (var t in criticalTables)
                {
                    var exists = await conn.ExecuteScalarAsync<bool>($"SELECT to_regclass('public.{t}') IS NOT NULL;");
                    exists.Should().BeTrue($"table public.{t} should exist after baseline");
                }

                // Reserved invariant: Guid.Empty row in platform_dimension_sets.
                var emptyExists = await conn.ExecuteScalarAsync<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM public.platform_dimension_sets WHERE dimension_set_id = '{Guid.Empty}');");
                emptyExists.Should().BeTrue();

                // Defense-in-depth: posted document header immutability trigger must exist.
                var trgPostedHeader = await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM pg_trigger WHERE tgname = 'trg_documents_posted_immutable');");
                trgPostedHeader.Should().BeTrue();

                var installedIndexes = (await conn.QueryAsync<string>(
                    "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname = ANY(@Names);",
                    new { Names = requiredIndexes })).ToArray();
                installedIndexes.Should().BeEquivalentTo(requiredIndexes,
                    "clean initialization must install all read paths without a repair pass");

                var obsoleteRelations = new[]
                {
                    "platform_report_runs", "platform_report_run_rows",
                    "ix_acc_balances_period_account", "ix_acc_turnovers_period_account",
                    "ix_opreg_dim_rules_register_ordinal", "ix_opreg_finalizations_register_period",
                    "ix_platform_audit_event_changes_event", "ix_refreg_dim_rules_register_ordinal"
                };
                (await conn.QueryAsync<string>(
                    "SELECT relname FROM pg_class WHERE relnamespace = 'public'::regnamespace AND relname = ANY(@Names);",
                    new { Names = obsoleteRelations })).Should().BeEmpty();
            }

            // Assert: provider-level schema validators succeed.
            var host = IntegrationHostFactory.Create(csb.ConnectionString);
            try
            {
                await host.StartAsync();

                await using var scope = host.Services.CreateAsyncScope();

                await scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>().ValidateAsync();
                await scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>().ValidateAsync();
                await scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>().ValidateAsync();
                await scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>().ValidateAsync();
            }
            finally
            {
                await host.StopAsync();

                if (host is IAsyncDisposable asyncHost)
                    await asyncHost.DisposeAsync();
                else
                    host.Dispose();
            }

            // Repair is tested only after Evolve-only schema validation has succeeded.
            await using (var conn = new NpgsqlConnection(csb.ConnectionString))
            {
                await conn.OpenAsync();
                var definitions = (await conn.QueryAsync<string>(
                    "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = ANY(@Names) ORDER BY indexname;",
                    new { Names = requiredIndexes })).ToArray();
                foreach (var index in requiredIndexes)
                    await conn.ExecuteAsync($"DROP INDEX public.\"{index}\";");

                await DatabaseBootstrapper.RepairAsync(csb.ConnectionString);
                await DatabaseBootstrapper.RepairAsync(csb.ConnectionString);
                (await conn.QueryAsync<string>(
                    "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = ANY(@Names) ORDER BY indexname;",
                    new { Names = requiredIndexes })).Should().Equal(definitions,
                    "explicit repair must restore the same index definitions and remain idempotent");
            }
        }
        finally
        {
            await DropDatabaseAsync(adminCsb.ConnectionString, dbName);
        }
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        if (databaseName.Contains('"'))
            throw new ArgumentException("Database name must not contain quotes.", nameof(databaseName));

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();

        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{databaseName}\";");
        await admin.ExecuteAsync($"CREATE DATABASE \"{databaseName}\";");
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        if (databaseName.Contains('"'))
            throw new ArgumentException("Database name must not contain quotes.", nameof(databaseName));

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();

        // Ensure the DB can be dropped even if a test failed before disposing all connections.
        await admin.ExecuteAsync(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @DbName;",
            new { DbName = databaseName });

        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{databaseName}\";");
    }
}
