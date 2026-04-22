using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Platform users (identity projection).
///
/// IMPORTANT:
/// - The platform is single-tenant per database (1 DB = 1 company).
/// - Users are a shadow/projection of the external IdP (Keycloak), keyed by auth_subject (sub).
/// - This table is NOT append-only (it may be upserted/updated as identities evolve).
/// </summary>
public sealed class PlatformUsersMigration : IDdlObject
{
    public string Name => "platform_users";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_users (
                                    user_id UUID PRIMARY KEY,

                                    auth_subject TEXT NOT NULL,

                                    email TEXT NULL,
                                    display_name TEXT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_users_auth_subject_nonempty CHECK (length(trim(auth_subject)) > 0),
                                    CONSTRAINT ck_platform_users_email_nonempty CHECK (email IS NULL OR length(trim(email)) > 0),
                                    CONSTRAINT ck_platform_users_display_name_nonempty CHECK (display_name IS NULL OR length(trim(display_name)) > 0)
                                );
                                """;
}
