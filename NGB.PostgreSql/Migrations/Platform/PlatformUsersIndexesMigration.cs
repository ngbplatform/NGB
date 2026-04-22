using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformUsersIndexesMigration : IDdlObject
{
    public string Name => "platform_users_indexes";

    public string Generate() => """
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_users_auth_subject
                                    ON platform_users(auth_subject);

                                CREATE INDEX IF NOT EXISTS ix_platform_users_email
                                    ON platform_users(email)
                                    WHERE email IS NOT NULL;
                                """;
}
