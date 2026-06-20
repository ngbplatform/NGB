using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformUserRolesMigration : IDdlObject
{
    public string Name => "platform_user_roles";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_user_roles (
                                    user_id UUID NOT NULL REFERENCES platform_users(user_id),
                                    role_id UUID NOT NULL REFERENCES platform_roles(role_id),
                                    assigned_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    assigned_by_user_id UUID NULL REFERENCES platform_users(user_id),

                                    PRIMARY KEY (user_id, role_id)
                                );

                                CREATE INDEX IF NOT EXISTS ix_platform_user_roles_role_id
                                    ON platform_user_roles(role_id);
                                """;
}
