using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Performance indexes for <c>document_relationships</c>.
///
/// Query patterns:
/// - list outgoing edges by from_document_id ordered by created_at_utc desc.
/// - list incoming edges by to_document_id ordered by created_at_utc desc.
/// - list outgoing/incoming edges by (document_id, relationship_code_norm) ordered by created_at_utc desc.
/// </summary>
public sealed class DocumentRelationshipsIndexesMigration : IDdlObject
{
    public string Name => "document_relationships.indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_docrel_from_created_id
                                    ON document_relationships (from_document_id, created_at_utc DESC, relationship_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_docrel_to_created_id
                                    ON document_relationships (to_document_id, created_at_utc DESC, relationship_id DESC);

                                -- These are used for common reads like:
                                --   WHERE from_document_id = @id AND relationship_code_norm = @codeNorm
                                --   ORDER BY created_at_utc DESC, relationship_id DESC
                                CREATE INDEX IF NOT EXISTS ix_docrel_from_code_created_id
                                    ON document_relationships (from_document_id, relationship_code_norm, created_at_utc DESC, relationship_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_docrel_to_code_created_id
                                    ON document_relationships (to_document_id, relationship_code_norm, created_at_utc DESC, relationship_id DESC);
                                """;
}
