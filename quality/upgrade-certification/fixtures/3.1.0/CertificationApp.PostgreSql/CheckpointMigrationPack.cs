using NGB.Persistence.Migrations;

namespace CertificationApp.PostgreSql;

public sealed class CheckpointMigrationPack : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "certification",
            MigrationAssemblies: [typeof(CheckpointMigrationPack).Assembly],
            DependsOn: ["platform"]);
    }
}
