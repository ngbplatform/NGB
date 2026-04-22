using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Defense in depth: forbid mutating <c>document_relationships</c> unless the "from" document is Draft.
///
/// Rationale:
/// - Application service in Runtime enforces the invariant, but direct SQL must be guarded as well.
/// - Relationships are modeled as immutable edges; UPDATE is forbidden.
///
/// Semantics:
/// - INSERT is allowed only when the referenced from-document exists and is Draft.
/// - DELETE is allowed only when both endpoint documents still exist and from-document is Draft.
/// - DELETE triggered by FK cascades is always allowed.
/// </summary>
public sealed class DocumentRelationshipsDraftGuardMigration : IDdlObject
{
    public string Name => "document_relationships.draft_guard";

    public string Generate() => """
                                CREATE OR REPLACE FUNCTION ngb_enforce_document_relationships_draft_from_document()
                                RETURNS trigger AS $$
                                DECLARE
                                    from_status smallint;
                                BEGIN
                                    -- Allow FK cascades (parent table trigger invokes DELETE on this table).
                                    -- pg_trigger_depth() > 1 reliably identifies such cases.
                                    IF TG_OP = 'DELETE' AND pg_trigger_depth() > 1 THEN
                                        RETURN OLD;
                                    END IF;

                                    IF TG_OP = 'UPDATE' THEN
                                        RAISE EXCEPTION 'Document relationships are immutable. Delete and recreate the edge.'
                                            USING ERRCODE = '55000';
                                    END IF;

                                    IF TG_OP = 'INSERT' THEN
                                        SELECT d.status INTO from_status
                                        FROM documents d
                                        WHERE d.id = NEW.from_document_id;

                                        -- 1 = DocumentStatus.Draft
                                        IF from_status IS DISTINCT FROM 1 THEN
                                            RAISE EXCEPTION 'Document relationships can only be mutated while the from-document is Draft.'
                                                USING ERRCODE = '55000';
                                        END IF;

                                        RETURN NEW;
                                    END IF;

                                    -- DELETE (direct statement)
                                    -- If either endpoint no longer exists, this is either a cleanup or a cascade path.
                                    -- We allow it to avoid blocking document deletions.
                                    IF NOT EXISTS (SELECT 1 FROM documents d WHERE d.id = OLD.from_document_id)
                                       OR NOT EXISTS (SELECT 1 FROM documents d WHERE d.id = OLD.to_document_id)
                                    THEN
                                        RETURN OLD;
                                    END IF;

                                    SELECT d.status INTO from_status
                                    FROM documents d
                                    WHERE d.id = OLD.from_document_id;

                                    IF from_status IS DISTINCT FROM 1 THEN
                                        RAISE EXCEPTION 'Document relationships can only be mutated while the from-document is Draft.'
                                            USING ERRCODE = '55000';
                                    END IF;

                                    RETURN OLD;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- (Re)install trigger deterministically.
                                DROP TRIGGER IF EXISTS trg_document_relationships_draft_guard ON public.document_relationships;
                                CREATE TRIGGER trg_document_relationships_draft_guard
                                BEFORE INSERT OR UPDATE OR DELETE ON public.document_relationships
                                FOR EACH ROW
                                EXECUTE FUNCTION ngb_enforce_document_relationships_draft_from_document();
                                """;
}
