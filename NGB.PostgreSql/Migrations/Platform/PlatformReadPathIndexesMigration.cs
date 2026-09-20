using NGB.Persistence.Migrations;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Restores current read-path indexes using the same SQL as the versioned upgrade.
/// Apply after the released DDL objects, which can recreate redundant indexes.
/// </summary>
public sealed class PlatformReadPathIndexesMigration : IDdlObject
{
    private const string ResourceName = "NGB.PostgreSql.db.migrations.V2026_08_26_0100__ngb_platform_read_path_indexes.sql";

    public string Name => "platform_read_path_indexes";

    public string Generate()
    {
        using var stream = typeof(PlatformReadPathIndexesMigration).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new NgbInvariantViolationException($"Missing embedded migration resource: {ResourceName}");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
