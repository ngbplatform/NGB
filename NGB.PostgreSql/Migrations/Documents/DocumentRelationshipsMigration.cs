using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Platform-level document relationships.
///
/// Goals:
/// - Provide a first-class, queryable graph of documents (directed edges).
/// - Keep storage simple and relational (no JSON payload).
/// - Ensure case-insensitive codes via generated *_code_norm.
/// - Keep referential integrity via FK to <c>documents</c> and cascade deletes on document deletion.
/// </summary>
public sealed class DocumentRelationshipsMigration : IDdlObject
{
    public string Name => "document_relationships";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS document_relationships (
                                    relationship_id             uuid         PRIMARY KEY,

                                    from_document_id            uuid         NOT NULL,
                                    to_document_id              uuid         NOT NULL,

                                    relationship_code           text         NOT NULL,
                                    relationship_code_norm      text         GENERATED ALWAYS AS (lower(btrim(relationship_code))) STORED,

                                    created_at_utc              timestamptz  NOT NULL DEFAULT NOW()
                                );

                                -- Drift repair (columns)
                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS relationship_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS from_document_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS to_document_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS relationship_code text;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'document_relationships'
                                          AND column_name = 'relationship_code_norm'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD COLUMN relationship_code_norm text GENERATED ALWAYS AS (lower(btrim(relationship_code))) STORED;
                                    END IF;
                                END
                                $$;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS created_at_utc timestamptz;

                                -- Drift repair (PK)
                                DO $$
                                BEGIN
                                    -- NOTE:
                                    -- We intentionally avoid enforcing a specific PK constraint name.
                                    -- If the table was created with an inline PRIMARY KEY, PostgreSQL will
                                    -- name it e.g. document_relationships_pkey. Adding another PK would fail.
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'document_relationships'
                                          AND c.contype = 'p'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT pk_document_relationships PRIMARY KEY (relationship_id);
                                    END IF;
                                END
                                $$;

                                -- Constraints
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_trimmed'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_trimmed
                                                CHECK (relationship_code = btrim(relationship_code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_nonempty'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_nonempty
                                                CHECK (length(relationship_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_len'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_len
                                                CHECK (length(relationship_code) <= 128);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_not_self'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_not_self
                                                CHECK (from_document_id <> to_document_id);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ux_document_relationships_triplet'
                                    ) THEN
                                        -- Note: PostgreSQL represents indexes as relations in the schema namespace.
                                        -- If a drift-repair test (or a manual DBA action) created a UNIQUE INDEX
                                        -- with the same contract name, attempting to ADD CONSTRAINT ... UNIQUE (...)
                                        -- would fail with 42P07 (relation already exists). To keep the migration
                                        -- strictly idempotent and drift-repairable, drop any conflicting index
                                        -- and recreate the constraint (which will re-create the index with the
                                        -- expected column order).
                                        IF EXISTS (
                                            SELECT 1
                                            FROM pg_class c
                                            JOIN pg_namespace n ON n.oid = c.relnamespace
                                            WHERE n.nspname = 'public'
                                              AND c.relkind = 'i'
                                              AND c.relname = 'ux_document_relationships_triplet'
                                        ) THEN
                                            DROP INDEX IF EXISTS public.ux_document_relationships_triplet;
                                        END IF;

                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ux_document_relationships_triplet
                                                UNIQUE (from_document_id, relationship_code_norm, to_document_id);
                                    END IF;
                                END
                                $$;
                                -- FKs
                                --
                                -- Respawn resets data but not schema; drift tests can rename constraints.
                                -- We enforce the required contract names AND ON DELETE CASCADE semantics
                                -- by re-creating the FKs on every bootstrap (migrations are serialized).
                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_docrel_from_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_docrel_to_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_document_relationships_from_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_document_relationships_to_document;

                                ALTER TABLE document_relationships
                                    ADD CONSTRAINT fk_document_relationships_from_document
                                        FOREIGN KEY (from_document_id)
                                            REFERENCES documents(id)
                                            ON DELETE CASCADE;

                                ALTER TABLE document_relationships
                                    ADD CONSTRAINT fk_document_relationships_to_document
                                        FOREIGN KEY (to_document_id)
                                            REFERENCES documents(id)
                                            ON DELETE CASCADE;

                                -- Default value drift repair for created_at_utc.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'document_relationships'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;
                                END
                                $$;
                                """;
}
