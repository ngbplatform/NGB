using Dapper;
using NGB.Metadata.Documents.Relationships;
using NGB.Metadata.Documents.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Documents;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Documents;

/// <summary>
/// PostgreSQL implementation of <see cref="IDocumentRelationshipsPhysicalSchemaHealthReader"/>.
///
/// This reader validates the *static* <c>document_relationships</c> table contract.
/// It does not attempt to repair drift (migrations handle that).
/// </summary>
public sealed class PostgresDocumentRelationshipsPhysicalSchemaHealthReader(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow,
    IDocumentTypeRegistry documentTypes)
    : IDocumentRelationshipsPhysicalSchemaHealthReader
{
    public async Task<DocumentRelationshipsPhysicalSchemaHealth> GetAsync(CancellationToken ct = default)
    {
        var snapshot = await schemaInspector.GetSnapshotAsync(ct);

        const string table = "document_relationships";

        var requiredCols = new[]
        {
            "relationship_id",
            "from_document_id",
            "to_document_id",
            "relationship_code",
            "relationship_code_norm",
            "created_at_utc"
        };

        var requiredIndexes = new[]
        {
            "ix_docrel_from_created_id",
            "ix_docrel_to_created_id",
            "ix_docrel_from_code_created_id",
            "ix_docrel_to_code_created_id",
            "ux_document_relationships_triplet",

            // Built-in cardinality guards (partial unique indexes)
            "ux_docrel_from_rev_of",
            "ux_docrel_from_created_from",
            "ux_docrel_from_supersedes",
            "ux_docrel_to_supersedes"
        };

        var requiredConstraints = new[]
        {
            "ck_document_relationships_code_trimmed",
            "ck_document_relationships_code_nonempty",
            "ck_document_relationships_code_len",
            "ck_document_relationships_not_self",
            "ux_document_relationships_triplet",
            "fk_document_relationships_from_document",
            "fk_document_relationships_to_document"
        };

        var exists = snapshot.Tables.Contains(table);

        var missingCols = exists
            ? GetMissingColumns(snapshot, table, requiredCols)
            : requiredCols;

        var missingIdx = exists
            ? GetMissingIndexes(snapshot, table, requiredIndexes)
            : requiredIndexes;

        await uow.EnsureConnectionOpenAsync(ct);

        var hasTrigger = exists && await HasTriggerAsync("trg_document_relationships_draft_guard", table, ct);
        var hasFunction = exists && await HasFunctionAsync("ngb_enforce_document_relationships_draft_from_document", ct);
        var hasMirroringComputeFunction = await HasFunctionAsync("ngb_compute_document_relationship_id", ct);
        var hasMirroringSyncFunction = await HasFunctionAsync("ngb_sync_mirrored_document_relationship", ct);
        var hasMirroringInstallerFunction = await HasFunctionAsync("ngb_install_mirrored_document_relationship_trigger", ct);

        var expectedBindings = PostgresMirroredDocumentRelationshipBindings.EnumerateExpected(documentTypes);
        var existingBindings = await PostgresMirroredDocumentRelationshipBindings.LoadExistingBindingsAsync(
            uow,
            expectedBindings
                .Select(x => x.TableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct);
        var missingMirroredBindings = PostgresMirroredDocumentRelationshipBindings.GetMissingBindings(expectedBindings, existingBindings);

        var missingConstraints = exists
            ? await GetMissingConstraintsAsync(table, requiredConstraints, ct)
            : requiredConstraints;

        return new DocumentRelationshipsPhysicalSchemaHealth(
            TableName: table,
            Exists: exists,
            MissingColumns: missingCols,
            MissingIndexes: missingIdx,
            MissingConstraints: missingConstraints,
            HasDraftGuardTrigger: hasTrigger,
            HasDraftGuardFunction: hasFunction,
            HasMirroringComputeFunction: hasMirroringComputeFunction,
            HasMirroringSyncFunction: hasMirroringSyncFunction,
            HasMirroringInstallerFunction: hasMirroringInstallerFunction,
            MissingMirroredTriggerBindings: missingMirroredBindings
        );
    }

    private static IReadOnlyList<string> GetMissingColumns(
        DbSchemaSnapshot snapshot,
        string table,
        IReadOnlyList<string> required)
    {
        if (!snapshot.ColumnsByTable.TryGetValue(table, out var cols))
            return required.ToArray();

        var set = cols.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var c in required)
        {
            if (!set.Contains(c))
                missing.Add(c);
        }

        return missing;
    }

    private static IReadOnlyList<string> GetMissingIndexes(
        DbSchemaSnapshot snapshot,
        string table,
        IReadOnlyList<string> required)
    {
        if (!snapshot.IndexesByTable.TryGetValue(table, out var idx))
            return required.ToArray();

        var set = idx.Select(i => i.IndexName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var i in required)
        {
            if (!set.Contains(i))
                missing.Add(i);
        }

        return missing;
    }

    private async Task<IReadOnlyList<string>> GetMissingConstraintsAsync(
        string table,
        IReadOnlyList<string> required,
        CancellationToken ct)
    {
        var rows = (await uow.Connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT c.conname
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = 'public'
                  AND t.relname = @table;
                """,
                new { table },
                transaction: uow.Transaction,
                cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var name in required)
        {
            if (!rows.Contains(name))
                missing.Add(name);
        }

        return missing;
    }

    private async Task<bool> HasFunctionAsync(string functionName, CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE p.proname = @name
                  AND n.nspname = 'public';
                """,
                new { name = functionName },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return exists > 0;
    }

    private async Task<bool> HasTriggerAsync(string triggerName, string tableName, CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                WHERE t.tgname = @trigger
                  AND NOT t.tgisinternal
                  AND ns.nspname = 'public'
                  AND cl.relname = @table;
                """,
                new { trigger = triggerName, table = tableName },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return exists > 0;
    }
}
