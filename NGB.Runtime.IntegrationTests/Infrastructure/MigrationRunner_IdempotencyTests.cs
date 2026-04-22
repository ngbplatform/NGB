using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class MigrationRunner_IdempotencyTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_Twice_DoesNotChangeSchemaSnapshot()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var inspector = scope.ServiceProvider.GetRequiredService<IDbSchemaInspector>();

        var before = await inspector.GetSnapshotAsync(CancellationToken.None);

        // Act: run the same migration/bootstrap set again.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        var after = await inspector.GetSnapshotAsync(CancellationToken.None);

        Normalize(after).Should().BeEquivalentTo(Normalize(before), options => options.WithStrictOrdering());
    }

    private static NormalizedSchemaSnapshot Normalize(DbSchemaSnapshot snapshot)
    {
        var tables = snapshot.Tables.OrderBy(x => x).ToArray();

        var columnsByTable = snapshot.ColumnsByTable
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<NormalizedColumn>)kvp.Value
                    .OrderBy(c => c.ColumnName)
                    .ThenBy(c => c.DbType)
                    .ThenBy(c => c.IsNullable)
                    .ThenBy(c => c.CharacterMaximumLength)
                    .Select(c => new NormalizedColumn(c.ColumnName, c.DbType, c.IsNullable, c.CharacterMaximumLength))
                    .ToArray()
            );

        var foreignKeysByTable = snapshot.ForeignKeysByTable
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<NormalizedForeignKey>)kvp.Value
                    .OrderBy(fk => fk.ConstraintName)
                    .ThenBy(fk => fk.ColumnName)
                    .ThenBy(fk => fk.ReferencedTableName)
                    .ThenBy(fk => fk.ReferencedColumnName)
                    .Select(fk => new NormalizedForeignKey(
                        fk.ConstraintName,
                        fk.ColumnName,
                        fk.ReferencedTableName,
                        fk.ReferencedColumnName))
                    .ToArray()
            );

        var indexesByTable = snapshot.IndexesByTable
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<NormalizedIndex>)kvp.Value
                    .OrderBy(ix => ix.IndexName)
                    .ThenBy(ix => ix.IsUnique)
                    .Select(ix => new NormalizedIndex(
                        ix.IndexName,
                        ix.IsUnique,
                        ix.ColumnNames.OrderBy(x => x).ToArray()))
                    .ToArray()
            );

        return new NormalizedSchemaSnapshot(tables, columnsByTable, foreignKeysByTable, indexesByTable);
    }

    private sealed record NormalizedSchemaSnapshot(
        IReadOnlyList<string> Tables,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedColumn>> ColumnsByTable,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedForeignKey>> ForeignKeysByTable,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedIndex>> IndexesByTable);

    private sealed record NormalizedColumn(
        string ColumnName,
        string DbType,
        bool IsNullable,
        int? CharacterMaximumLength);

    private sealed record NormalizedForeignKey(
        string ConstraintName,
        string ColumnName,
        string ReferencedTableName,
        string ReferencedColumnName);

    private sealed record NormalizedIndex(
        string IndexName,
        bool IsUnique,
        IReadOnlyList<string> ColumnNames);
}
