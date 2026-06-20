using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformUserAccessVersionsMigration : IDdlObject
{
    public string Name => "platform_user_access_versions";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_user_access_versions (
                                    user_id UUID PRIMARY KEY REFERENCES platform_users(user_id),
                                    version BIGINT NOT NULL DEFAULT 1,
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                                );
                                """;
}
