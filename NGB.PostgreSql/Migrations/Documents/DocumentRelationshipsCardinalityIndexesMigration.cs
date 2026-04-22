using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// DB-level cardinality enforcement for selected built-in relationship codes.
///
/// Relationship types are extensible via Definitions and cardinality is enforced in runtime for all
/// types. Here we add partial unique indexes for a small set of well-known platform codes where DB
/// enforcement materially improves data integrity.
/// </summary>
public sealed class DocumentRelationshipsCardinalityIndexesMigration : IDdlObject
{
    public string Name => "document_relationships_cardinality_indexes";

    public string Generate() => """
                                -- reversal_of: each document can be a reversal of at most one other document
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_rev_of
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'reversal_of';

                                -- created_from: each document can be created from at most one source document
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_created_from
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'created_from';

                                -- supersedes: one-to-one
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_supersedes
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'supersedes';

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_to_supersedes
                                    ON document_relationships (to_document_id)
                                    WHERE relationship_code_norm = 'supersedes';
                                """;
}
