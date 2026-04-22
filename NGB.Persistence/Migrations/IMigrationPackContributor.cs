namespace NGB.Persistence.Migrations;

/// <summary>
/// Contributes one or more schema migration packs.
///
/// Intended usage:
/// - Platform assembly contributes the "platform" pack.
/// - Each vertical module contributes its own pack and declares dependency on "platform".
///
/// Packs can be discovered via reflection in a standalone migrator (K8s job / CI step).
/// </summary>
public interface IMigrationPackContributor
{
    IEnumerable<MigrationPack> GetPacks();
}
