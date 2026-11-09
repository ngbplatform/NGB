using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations.Platform;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

internal sealed class ReferenceRegisterRecorderCancellationMigration : IDdlObject
{
    private const string ResourceName = "NGB.PostgreSql.db.migrations.V2026_11_09_0100__reference_register_recorder_cancellation.sql";

    public string Name => "reference_register_recorder_cancellation";

    public string Generate() => PlatformReadPathIndexesMigration.ReadEmbeddedSql(ResourceName);
}
