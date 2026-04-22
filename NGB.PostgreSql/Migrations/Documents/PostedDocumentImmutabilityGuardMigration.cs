using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Defense in depth: forbid mutating typed document storages when the document is already posted.
///
/// This guard is intentionally implemented in the database to protect the platform against:
/// - future regressions in application-layer services
/// - accidental direct SQL modifications
///
/// Convention: any typed document table in public schema that starts with 'doc_' and contains a 'document_id' column
/// is protected by a reusable trigger named 'trg_posted_immutable'.
///
/// The installer function 'ngb_install_typed_document_immutability_guards()' can be re-run any time (idempotent) and
/// is used both during bootstrap and when new typed tables are created later.
/// </summary>
public sealed class PostedDocumentImmutabilityGuardMigration : IDdlObject
{
    public string Name => "documents.posted_immutability_guard";

    public string Generate() => """
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document()
                                RETURNS trigger AS $$
                                DECLARE
                                    doc_id uuid;
                                    st smallint;
                                BEGIN
                                    -- For INSERT/UPDATE we have NEW; for DELETE only OLD exists.
                                    IF TG_OP = 'DELETE' THEN
                                        doc_id := OLD.document_id;
                                    ELSE
                                        doc_id := NEW.document_id;
                                    END IF;

                                    SELECT d.status INTO st
                                    FROM documents d
                                    WHERE d.id = doc_id;

                                    IF COALESCE(st, 0) = 2 THEN
                                        RAISE EXCEPTION 'Document is posted and immutable: %', doc_id
                                            USING ERRCODE = '55000';
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                CREATE OR REPLACE FUNCTION ngb_install_typed_document_immutability_guards()
                                RETURNS void AS $$
                                DECLARE
                                    r record;
                                BEGIN
                                    FOR r IN
                                        SELECT DISTINCT c.table_schema, c.table_name
                                        FROM information_schema.columns c
                                        WHERE c.table_schema = 'public'
                                          AND c.column_name = 'document_id'
                                          AND c.table_name LIKE 'doc\_%' ESCAPE '\'
                                        ORDER BY c.table_name
                                    LOOP
                                        -- Table may have been dropped between snapshot and installation.
                                        IF to_regclass(format('%I.%I', r.table_schema, r.table_name)) IS NULL THEN
                                            CONTINUE;
                                        END IF;

                                        IF NOT EXISTS (
                                            SELECT 1
                                            FROM pg_trigger t
                                            JOIN pg_class cl ON cl.oid = t.tgrelid
                                            JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                                            WHERE t.tgname = 'trg_posted_immutable'
                                              AND ns.nspname = r.table_schema
                                              AND cl.relname = r.table_name
                                        ) THEN
                                            EXECUTE format(
                                                'CREATE TRIGGER trg_posted_immutable BEFORE INSERT OR UPDATE OR DELETE ON %I.%I FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();',
                                                r.table_schema,
                                                r.table_name
                                            );
                                        END IF;
                                    END LOOP;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- Cleanup: drop old GJE-specific trigger names (if any),
                                -- then ensure the reusable trigger exists on all typed tables.
                                DO $$
                                BEGIN
                                    IF to_regclass('public.doc_general_journal_entry') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_posted_immutable ON public.doc_general_journal_entry;
                                    END IF;

                                    IF to_regclass('public.doc_general_journal_entry__lines') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_lines_posted_immutable ON public.doc_general_journal_entry__lines;
                                    END IF;

                                    IF to_regclass('public.doc_general_journal_entry__allocations') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_alloc_posted_immutable ON public.doc_general_journal_entry__allocations;
                                    END IF;
                                END $$;

                                SELECT ngb_install_typed_document_immutability_guards();
                                """;
}
