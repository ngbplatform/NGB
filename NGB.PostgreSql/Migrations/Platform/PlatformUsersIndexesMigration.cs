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

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_users_normalized_email
                                    ON platform_users(lower(trim(email)))
                                    WHERE email IS NOT NULL;

                                CREATE INDEX IF NOT EXISTS ix_platform_users_display_sort
                                    ON platform_users(lower(coalesce(display_name, email, auth_subject)), user_id);
                                """;
}
