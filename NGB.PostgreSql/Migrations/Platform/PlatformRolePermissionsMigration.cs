using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformRolePermissionsMigration : IDdlObject
{
    public string Name => "platform_role_permissions";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_role_permissions (
                                    role_id UUID NOT NULL REFERENCES platform_roles(role_id),
                                    resource_kind TEXT NOT NULL,
                                    resource_code TEXT NOT NULL,
                                    action_code TEXT NOT NULL,
                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    PRIMARY KEY (role_id, resource_kind, resource_code, action_code),

                                    CONSTRAINT ck_platform_role_permissions_resource_kind_nonempty CHECK (length(trim(resource_kind)) > 0),
                                    CONSTRAINT ck_platform_role_permissions_resource_code_nonempty CHECK (length(trim(resource_code)) > 0),
                                    CONSTRAINT ck_platform_role_permissions_action_code_nonempty CHECK (length(trim(action_code)) > 0)
                                );

                                CREATE INDEX IF NOT EXISTS ix_platform_role_permissions_permission
                                    ON platform_role_permissions(resource_kind, resource_code, action_code);
                                """;
}
