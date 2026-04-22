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
[Collection(PostgresCollection.Name)]
public sealed class NgbSchemaBaseline_Platform_NoRepair_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InitializeAsync_UsingEvolveBaseline_CreatesCoreSchema_AndValidatorsPass()
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
            // Act: Evolve-only initialization.
            await DatabaseBootstrapper.InitializeAsync(csb.ConnectionString);

            // Assert: a few critical core tables exist.
            await using (var conn = new NpgsqlConnection(csb.ConnectionString))
            {
                await conn.OpenAsync();

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
