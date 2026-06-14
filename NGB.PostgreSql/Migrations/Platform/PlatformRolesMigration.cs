using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformRolesMigration : IDdlObject
{
    public string Name => "platform_roles";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_roles (
                                    role_id UUID PRIMARY KEY,
                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,
                                    description TEXT NULL,
                                    is_system BOOLEAN NOT NULL DEFAULT FALSE,
                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_roles_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_platform_roles_name_nonempty CHECK (length(trim(name)) > 0)
                                );

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_roles_code_norm
                                    ON platform_roles(lower(trim(code)));
                                """;
}
