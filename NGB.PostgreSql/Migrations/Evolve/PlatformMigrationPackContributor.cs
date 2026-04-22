using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;

namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Platform pack (core schema).
/// </summary>
public sealed class PlatformMigrationPackContributor : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "platform",
            MigrationAssemblies: [typeof(DatabaseBootstrapper).Assembly],
            DependsOn: [],
            RepairAsync: DatabaseBootstrapper.RepairAsync,
            RepairWithOptionsAsync: DatabaseBootstrapper.RepairAsync);
    }
}
