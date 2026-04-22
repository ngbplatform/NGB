using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Defense in depth: forbid mutating the common document header (table: documents) while the document is posted.
///
/// Rationale:
/// - Typed storages (doc_*) are protected by <see cref="PostedDocumentImmutabilityGuardMigration"/>.
/// - The common header (documents) must also be protected against direct SQL bypass.
///
/// Semantics:
/// - When OLD.status = Posted, UPDATE is allowed only when it changes *lifecycle fields* (status/posted_at/updated_at).
/// - DELETE is always forbidden for Posted.
///
/// Note: this guard is intentionally strict and is expected to work together with application-layer invariants.
/// </summary>
public sealed class PostedDocumentHeaderImmutabilityGuardMigration : IDdlObject
{
    public string Name => "documents.posted_header_immutability_guard";

    public string Generate() => """
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document_header()
                                RETURNS trigger AS $$
                                BEGIN
                                    -- 2 = DocumentStatus.Posted
                                    IF COALESCE(OLD.status, 0) = 2 THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Document is posted and immutable: %', OLD.id
                                                USING ERRCODE = '55000';
                                        END IF;

                                        -- UPDATE: allow only lifecycle fields while the document remains posted.
                                        -- If status changes away from Posted (unpost), we allow it as well,
                                        -- but still forbid changing any non-lifecycle fields in the same statement.
                                        IF (NEW.id <> OLD.id)
                                           OR (NEW.type_code IS DISTINCT FROM OLD.type_code)
                                           OR (NEW.date_utc IS DISTINCT FROM OLD.date_utc)
                                           OR (NEW.number IS DISTINCT FROM OLD.number)
                                           OR (NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc)
                                        THEN
                                            RAISE EXCEPTION 'Document is posted and immutable: %', OLD.id
                                                USING ERRCODE = '55000';
                                        END IF;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- (Re)install trigger deterministically.
                                DROP TRIGGER IF EXISTS trg_documents_posted_immutable ON public.documents;
                                CREATE TRIGGER trg_documents_posted_immutable
                                BEFORE UPDATE OR DELETE ON public.documents
                                FOR EACH ROW
                                EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document_header();
                                """;
}
