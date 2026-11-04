CREATE TABLE cat_certification_checkpoint (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display text NOT NULL
);

CREATE TABLE certification_checkpoint_effects (
    operation_id uuid PRIMARY KEY,
    captured_at_utc timestamptz NOT NULL DEFAULT now()
);

INSERT INTO platform_roles (role_id, code, name, description, is_system, is_active, created_at_utc, updated_at_utc)
VALUES (
    '019a0c12-8000-7000-8000-000000000001',
    'certification.administrator',
    'Administrator',
    'Application administrator registered by code.',
    true,
    true,
    now(),
    now()
);
