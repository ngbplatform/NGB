using Dapper;
using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Accounting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Migrations;

[Collection(SchemaPostgresCollection.Name)]
public sealed class AccountingPostingBoundaryMigrationTests(SchemaPostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string MigrationResource =
        "NGB.PostgreSql.db.migrations.V2026_11_08_0100__accounting_posting_boundary.sql";

    [Fact]
    public async Task Upgrade_PreservesLegacyHistoryAndSnapshots_AndRejectsEarlierSqlWrites()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        // This fixture owns a disposable database and rebuilds its schema before every test.
        await connection.ExecuteAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
        var assembly = typeof(DatabaseBootstrapper).Assembly;
        var previousMigrator = new EvolveDb.Evolve(connection)
        {
            IsEraseDisabled = true,
            EnableClusterMode = false,
            Schemas = ["public"],
            MetadataTableName = "migration_changelog__platform",
            EmbeddedResourceAssemblies = [assembly],
            EmbeddedResourceFilters = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("NGB.PostgreSql.db.migrations.V", StringComparison.Ordinal)
                    && name.EndsWith(".sql", StringComparison.Ordinal)
                    && name != MigrationResource)
                .ToArray()
        };
        previousMigrator.Migrate();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day15Utc.AddMonths(1), "50", "90.1", 100m);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 2, 1));

        const string insertRegister = """
            INSERT INTO accounting_register_main
                (document_id, period, debit_account_id, credit_account_id, amount, is_storno)
            VALUES (@DocumentId, @Period, @CashId, @RevenueId, 50, FALSE);
            """;
        var legacyDocumentId = Guid.CreateVersion7();
        var parameters = new
        {
            DocumentId = legacyDocumentId,
            Period = ReportingTestHelpers.Day15Utc,
            CashId = cashId,
            RevenueId = revenueId
        };
        // The old guard accepts this real ledger write, leaving the already closed snapshot stale.
        await connection.ExecuteAsync(insertRegister, parameters);
        await connection.ExecuteAsync("""
            INSERT INTO accounting_turnovers (period, account_id, dimension_set_id, debit_amount, credit_amount)
            VALUES ('2026-01-01', @CashId, @Empty, 50, 0), ('2026-01-01', @RevenueId, @Empty, 0, 50);
            """, new { CashId = cashId, RevenueId = revenueId, Empty = Guid.Empty });
        var before = await ReadAccountingStateAsync(connection);
        var checksums = (await connection.QueryAsync<string>(
            "SELECT version || ':' || checksum FROM migration_changelog__platform WHERE checksum IS NOT NULL;")).ToArray();

        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);
        await DatabaseBootstrapper.InitializeAsync(Fixture.ConnectionString);

        (await ReadAccountingStateAsync(connection)).Should().Equal(before);
        (await connection.QueryAsync<string>(
            "SELECT version || ':' || checksum FROM migration_changelog__platform WHERE checksum IS NOT NULL;"))
            .Should().Contain(checksums);

        // The versioned migration must enforce the rule without relying on a repair pass.
        Func<Task> insertEarlier = async () => await connection.ExecuteAsync(insertRegister,
            new { DocumentId = Guid.CreateVersion7(), parameters.Period, parameters.CashId, parameters.RevenueId });
        await insertEarlier.Should().ThrowAsync<PostgresException>().WithMessage("*later period is closed*2026-02-01*");
        Func<Task> moveLegacyForward = async () => await connection.ExecuteAsync(
            "UPDATE accounting_register_main SET period = '2026-03-15 00:00:00+00' WHERE document_id = @legacyDocumentId;",
            new { legacyDocumentId });
        await moveLegacyForward.Should().ThrowAsync<PostgresException>().WithMessage("*later period is closed*");
        Func<Task> deleteLegacy = async () => await connection.ExecuteAsync(
            "DELETE FROM accounting_register_main WHERE document_id = @legacyDocumentId;", new { legacyDocumentId });
        await deleteLegacy.Should().ThrowAsync<PostgresException>().WithMessage("*later period is closed*");
        Func<Task> updateEarlierTurnovers = async () => await connection.ExecuteAsync(
            "UPDATE accounting_turnovers SET debit_amount = debit_amount + 1 WHERE period = '2026-01-01';");
        await updateEarlierTurnovers.Should().ThrowAsync<PostgresException>().WithMessage("*later period is closed*");
        (await ReadAccountingStateAsync(connection)).Should().Equal(before);

        await DatabaseBootstrapper.RepairAsync(Fixture.ConnectionString);
        (await ReadAccountingStateAsync(connection)).Should().Equal(before);
        await insertEarlier.Should().ThrowAsync<PostgresException>().WithMessage("*later period is closed*");

        await connection.ExecuteAsync(insertRegister, new
        {
            DocumentId = Guid.CreateVersion7(),
            Period = ReportingTestHelpers.Day15Utc.AddMonths(2),
            parameters.CashId,
            parameters.RevenueId
        });
    }

    [Fact]
    public async Task RepairDefinition_MatchesVersionedMigration()
    {
        await using var stream = typeof(DatabaseBootstrapper).Assembly.GetManifestResourceStream(MigrationResource)!;
        using var reader = new StreamReader(stream);
        var migration = await reader.ReadToEndAsync();
        new AccountingPostingBoundaryGuardMigration().Generate().Trim().Should().Be(migration.Trim());
    }

    private static async Task<string[]> ReadAccountingStateAsync(NpgsqlConnection connection)
    {
        return (await connection.QueryAsync<string>("""
            SELECT 'register:' || row_to_json(r)::text FROM accounting_register_main r
            UNION ALL SELECT 'turnovers:' || row_to_json(t)::text FROM accounting_turnovers t
            UNION ALL SELECT 'balances:' || row_to_json(b)::text FROM accounting_balances b
            UNION ALL SELECT 'closed:' || row_to_json(c)::text FROM accounting_closed_periods c
            ORDER BY 1;
            """)).ToArray();
    }
}
