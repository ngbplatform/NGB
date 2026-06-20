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

CREATE TABLE IF NOT EXISTS platform_user_roles (
    user_id UUID NOT NULL REFERENCES platform_users(user_id),
    role_id UUID NOT NULL REFERENCES platform_roles(role_id),
    assigned_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    assigned_by_user_id UUID NULL REFERENCES platform_users(user_id),

    PRIMARY KEY (user_id, role_id)
);

CREATE INDEX IF NOT EXISTS ix_platform_user_roles_role_id
    ON platform_user_roles(role_id);

CREATE TABLE IF NOT EXISTS platform_user_access_versions (
    user_id UUID PRIMARY KEY REFERENCES platform_users(user_id),
    version BIGINT NOT NULL DEFAULT 1,
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

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
