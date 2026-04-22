using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Document registry (common header) shared by all document types.
///
/// Design (hybrid model):
/// - Common fields live here (id, type_code, date, status, timestamps).
/// - Per-type header/table parts live in separate tables with naming convention:
///     doc_{type_code}
///     doc_{type_code}__{part}
///   Example: doc_payment, doc_payment__lines, doc_payment__transactions
///
/// No payload_json by design (typed per-document tables are preferred).
/// </summary>
public sealed class DocumentsMigration : IDdlObject
{
    public string Name => "documents";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS documents (
                                    id                          uuid         PRIMARY KEY,
                                    type_code                   text         NOT NULL,
                                    number                      text         NULL,

                                    date_utc                    timestamptz  NOT NULL,

                                    status                      smallint     NOT NULL,
                                    posted_at_utc               timestamptz  NULL,
                                    marked_for_deletion_at_utc  timestamptz  NULL,

                                    created_at_utc              timestamptz  NOT NULL DEFAULT NOW(),
                                    updated_at_utc              timestamptz  NOT NULL DEFAULT NOW(),

                                    -- Status values match NGB.Core.Documents.DocumentStatus:
                                    -- 1 = Draft, 2 = Posted, 3 = MarkedForDeletion
                                    CONSTRAINT ck_documents_status
                                        CHECK (status IN (1, 2, 3)),

                                    CONSTRAINT ck_documents_posted_state
                                        CHECK (
                                            (status = 2 AND posted_at_utc IS NOT NULL AND marked_for_deletion_at_utc IS NULL)
                                            OR
                                            (status <> 2 AND posted_at_utc IS NULL)
                                        ),

                                    CONSTRAINT ck_documents_marked_for_deletion_state
                                        CHECK (
                                            (status = 3 AND marked_for_deletion_at_utc IS NOT NULL AND posted_at_utc IS NULL)
                                            OR
                                            (status <> 3 AND marked_for_deletion_at_utc IS NULL)
                                        )
                                );

                                -- Timestamp default drift repair:
                                -- for timestamptz "instants" we prefer DEFAULT NOW() (not "NOW() at time zone 'UTC'").
                                DO $$
                                BEGIN
                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'documents'
                                       AND column_name  = 'created_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE documents ALTER COLUMN created_at_utc SET DEFAULT NOW()';
                                  END IF;

                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'documents'
                                       AND column_name  = 'updated_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE documents ALTER COLUMN updated_at_utc SET DEFAULT NOW()';
                                  END IF;
                                END $$;
                                """;
}
