-- Additive resources: intentionally no foreign key to a polymorphic parent object.
CREATE TABLE IF NOT EXISTS platform_attachments (
    id uuid PRIMARY KEY,
    object_kind smallint NOT NULL CHECK (object_kind IN (1, 2, 3)),
    object_type_code text NOT NULL CHECK (length(object_type_code) BETWEEN 1 AND 200),
    object_id uuid NOT NULL,
    file_name text NOT NULL CHECK (length(file_name) BETWEEN 1 AND 255),
    content_type text NOT NULL CHECK (length(content_type) BETWEEN 1 AND 200),
    size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
    storage_object_key text NOT NULL UNIQUE,
    upload_object_key text NOT NULL UNIQUE,
    status smallint NOT NULL CHECK (status IN (1, 2, 3)),
    created_at_utc timestamptz NOT NULL,
    created_by_user_id uuid NOT NULL REFERENCES platform_users(user_id),
    upload_expires_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz,
    deleted_at_utc timestamptz,
    deleted_by_user_id uuid REFERENCES platform_users(user_id),
    storage_deleted_at_utc timestamptz,
    CHECK (status <> 2 OR completed_at_utc IS NOT NULL),
    CHECK ((status = 3) = (deleted_at_utc IS NOT NULL)),
    CHECK (storage_deleted_at_utc IS NULL OR status = 3)
);
CREATE INDEX IF NOT EXISTS ix_platform_attachments_object ON platform_attachments
    (object_kind, object_type_code, object_id, status, id DESC) WHERE status IN (1, 2);
CREATE INDEX IF NOT EXISTS ix_platform_attachments_pending ON platform_attachments
    (created_at_utc, id) WHERE status = 1;

CREATE TABLE IF NOT EXISTS platform_notes (
    id uuid PRIMARY KEY,
    object_kind smallint NOT NULL CHECK (object_kind IN (1, 2, 3)),
    object_type_code text NOT NULL CHECK (length(object_type_code) BETWEEN 1 AND 200),
    object_id uuid NOT NULL,
    text text NOT NULL CHECK (length(text) BETWEEN 1 AND 100000),
    version bigint NOT NULL CHECK (version > 0),
    created_at_utc timestamptz NOT NULL,
    created_by_user_id uuid NOT NULL REFERENCES platform_users(user_id),
    updated_at_utc timestamptz,
    updated_by_user_id uuid REFERENCES platform_users(user_id),
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at_utc timestamptz,
    deleted_by_user_id uuid REFERENCES platform_users(user_id),
    CHECK (is_deleted = (deleted_at_utc IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS ix_platform_notes_object ON platform_notes
    (object_kind, object_type_code, object_id, id DESC) WHERE NOT is_deleted;
