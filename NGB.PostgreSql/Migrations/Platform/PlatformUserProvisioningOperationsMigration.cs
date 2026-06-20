using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformUserProvisioningOperationsMigration : IDdlObject
{
    public string Name => "platform_user_provisioning_operations";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_user_provisioning_operations (
                                    operation_id UUID PRIMARY KEY,
                                    operation_type TEXT NOT NULL,
                                    requested_email TEXT NULL,
                                    keycloak_user_id TEXT NULL,
                                    platform_user_id UUID NULL REFERENCES platform_users(user_id),
                                    status TEXT NOT NULL,
                                    error TEXT NULL,
                                    requested_by_user_id UUID NULL REFERENCES platform_users(user_id),
                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_user_provisioning_operations_type_nonempty CHECK (length(trim(operation_type)) > 0),
                                    CONSTRAINT ck_platform_user_provisioning_operations_status_nonempty CHECK (length(trim(status)) > 0)
                                );
                                """;
}
