using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(SchemaPostgresCollection.Name)]
public sealed class PostgresSchemaInspector_IndexOrder_IsPreserved_P2Tests(SchemaPostgresTestFixture fixture)
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsIndexColumnsInDefinedOrder()
    {
        await fixture.ResetDatabaseAsync();

        await ExecuteAsync(fixture.ConnectionString, """
        DROP INDEX IF EXISTS ix_it_idx_order__abc;
        DROP TABLE IF EXISTS it_idx_order;

        CREATE TABLE it_idx_order (
            a int NOT NULL,
            b int NOT NULL,
            c int NOT NULL
        );

        CREATE INDEX ix_it_idx_order__abc
            ON it_idx_order(a, b, c);
        """);

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var inspector = scope.ServiceProvider.GetRequiredService<IDbSchemaInspector>();

        var snapshot = await inspector.GetSnapshotAsync(CancellationToken.None);

        snapshot.IndexesByTable.TryGetValue("it_idx_order", out var indexes).Should().BeTrue();
        indexes.Should().NotBeNull();

        var ix = indexes!.Single(i => i.IndexName.Equals("ix_it_idx_order__abc", StringComparison.OrdinalIgnoreCase));
        ix.ColumnNames.Should().Equal(new[] { "a", "b", "c" });
    }

    [Fact]
    public async Task SnapshotScope_ReusesOneSnapshot_AndRefreshesAfterDisposal()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var inspector = scope.ServiceProvider.GetRequiredService<IDbSchemaInspector>();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IDbSchemaSnapshotScopeFactory>();

        DbSchemaSnapshot first;
        await using (await scopeFactory.BeginSnapshotScopeAsync(CancellationToken.None))
        {
            first = await inspector.GetSnapshotAsync(CancellationToken.None);
            first.DatabaseObjects.Should().NotBeNull();
            await ExecuteAsync(fixture.ConnectionString, "CREATE TABLE it_snapshot_refresh (id int NOT NULL);");

            var shared = await inspector.GetSnapshotAsync(CancellationToken.None);
            shared.Should().BeSameAs(first);
            shared.Tables.Should().NotContain("it_snapshot_refresh");
        }

        var refreshed = await inspector.GetSnapshotAsync(CancellationToken.None);
        refreshed.Should().NotBeSameAs(first);
        refreshed.Tables.Should().Contain("it_snapshot_refresh");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
