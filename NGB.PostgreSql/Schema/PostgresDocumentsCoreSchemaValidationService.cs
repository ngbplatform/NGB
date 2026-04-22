using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Schema.Internal;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// PostgreSQL validator for the Documents core schema.
///
/// This validator focuses on correctness/safety invariants that, if broken,
/// can corrupt document lifecycle flows or linked document graphs.
/// </summary>
public sealed class PostgresDocumentsCoreSchemaValidationService(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow,
    IDocumentTypeRegistry documentTypes,
    ILogger<PostgresDocumentsCoreSchemaValidationService> logger)
    : IDocumentsCoreSchemaValidationService
{
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var snapshot = await schemaInspector.GetSnapshotAsync(ct);
        var errors = new List<string>();

        // Core tables
        PostgresSchemaValidationChecks.RequireTable(snapshot, "documents", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "document_relationships", errors);

        // document_relationships minimal column contract
        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "document_relationships",
            required:
            [
                "relationship_id",
                "from_document_id",
                "to_document_id",
                "relationship_code",
                "relationship_code_norm",
                "created_at_utc"
            ],
            errors);

        // Performance / uniqueness indexes (names are part of the contract; migrations are idempotent)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ix_docrel_from_created_id", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ix_docrel_to_created_id", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ix_docrel_from_code_created_id", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ix_docrel_to_code_created_id", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ux_document_relationships_triplet", errors);

        // Built-in cardinality guards (partial unique indexes)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ux_docrel_from_rev_of", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ux_docrel_from_created_from", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ux_docrel_from_supersedes", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "document_relationships", "ux_docrel_to_supersedes", errors);

        // FKs
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "document_relationships", "from_document_id", "documents", "id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "document_relationships", "to_document_id", "documents", "id", errors);

        // Guards and constraint names (defense in depth)
        await uow.EnsureConnectionOpenAsync(ct);

        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_enforce_document_relationships_draft_from_document", errors, ct);
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_compute_document_relationship_id", errors, ct);
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_sync_mirrored_document_relationship", errors, ct);
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_install_mirrored_document_relationship_trigger", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_document_relationships_draft_guard", "document_relationships", errors, ct);

        var expectedMirroredBindings = PostgresMirroredDocumentRelationshipBindings.EnumerateExpected(documentTypes);
        var existingMirroredBindings = await PostgresMirroredDocumentRelationshipBindings.LoadExistingBindingsAsync(
            uow,
            expectedMirroredBindings
                .Select(x => x.TableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct);

        foreach (var missingBinding in PostgresMirroredDocumentRelationshipBindings.GetMissingBindings(
                     expectedMirroredBindings, existingMirroredBindings))
        {
            errors.Add($"Missing mirrored relationship trigger binding: {missingBinding}");
        }

        // Check constraints (names are part of the contract)
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "ck_document_relationships_code_trimmed", "document_relationships", errors, ct);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "ck_document_relationships_code_nonempty", "document_relationships", errors, ct);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "ck_document_relationships_code_len", "document_relationships", errors, ct);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "ck_document_relationships_not_self", "document_relationships", errors, ct);

        // Uniqueness + FK constraint names (helps detect drift where a differently named constraint exists)
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "ux_document_relationships_triplet", "document_relationships", errors, ct);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "fk_document_relationships_from_document", "document_relationships", errors, ct);
        await PostgresSchemaValidationChecks.RequireConstraintAsync(uow, "fk_document_relationships_to_document", "document_relationships", errors, ct);

        if (errors.Count > 0)
        {
            logger.LogError(
                "Documents core schema validation FAILED with {ErrorCount} errors in {ElapsedMs} ms.",
                errors.Count,
                sw.ElapsedMilliseconds);

            throw new DocumentSchemaValidationException("Documents core schema validation failed:\n- " + string.Join("\n- ", errors));
        }

        logger.LogInformation("Documents core schema validation OK in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
    }
}
