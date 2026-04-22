using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Documents;

public sealed class PostgresDocumentRelationshipRepository(IUnitOfWork uow) : IDocumentRelationshipRepository
{
    public async Task<bool> TryCreateAsync(DocumentRelationshipRecord relationship, CancellationToken ct = default)
    {
        if (relationship is null)
            throw new NgbArgumentRequiredException(nameof(relationship));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO document_relationships
                               (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
                           VALUES
                               (@Id, @FromDocumentId, @ToDocumentId, @RelationshipCode, @CreatedAtUtc)
                           ON CONFLICT (relationship_id) DO NOTHING;
                           """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(sql, relationship, uow.Transaction, cancellationToken: ct));

        return rows == 1;
    }

    public async Task<DocumentRelationshipRecord?> GetAsync(Guid relationshipId, CancellationToken ct = default)
    {
        relationshipId.EnsureRequired(nameof(relationshipId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               relationship_id AS Id,
                               from_document_id AS FromDocumentId,
                               to_document_id AS ToDocumentId,
                               relationship_code AS RelationshipCode,
                               relationship_code_norm AS RelationshipCodeNorm,
                               created_at_utc AS CreatedAtUtc
                           FROM document_relationships
                           WHERE relationship_id = @relationshipId;
                           """;

        return await uow.Connection.QuerySingleOrDefaultAsync<DocumentRelationshipRecord>(
            new CommandDefinition(sql, new { relationshipId }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<DocumentRelationshipRecord?> GetSingleOutgoingByCodeNormAsync(
        Guid fromDocumentId,
        string relationshipCodeNorm,
        CancellationToken ct = default)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        if (string.IsNullOrWhiteSpace(relationshipCodeNorm))
            throw new NgbArgumentRequiredException(nameof(relationshipCodeNorm));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               relationship_id AS Id,
                               from_document_id AS FromDocumentId,
                               to_document_id AS ToDocumentId,
                               relationship_code AS RelationshipCode,
                               relationship_code_norm AS RelationshipCodeNorm,
                               created_at_utc AS CreatedAtUtc
                           FROM document_relationships
                           WHERE from_document_id = @fromDocumentId
                             AND relationship_code_norm = @relationshipCodeNorm
                           LIMIT 1;
                           """;

        return await uow.Connection.QuerySingleOrDefaultAsync<DocumentRelationshipRecord>(
            new CommandDefinition(sql, new { fromDocumentId, relationshipCodeNorm }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<DocumentRelationshipRecord?> GetSingleIncomingByCodeNormAsync(
        Guid toDocumentId,
        string relationshipCodeNorm,
        CancellationToken ct = default)
    {
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        if (string.IsNullOrWhiteSpace(relationshipCodeNorm))
            throw new NgbArgumentRequiredException(nameof(relationshipCodeNorm));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               relationship_id AS Id,
                               from_document_id AS FromDocumentId,
                               to_document_id AS ToDocumentId,
                               relationship_code AS RelationshipCode,
                               relationship_code_norm AS RelationshipCodeNorm,
                               created_at_utc AS CreatedAtUtc
                           FROM document_relationships
                           WHERE to_document_id = @toDocumentId
                             AND relationship_code_norm = @relationshipCodeNorm
                           LIMIT 1;
                           """;

        return await uow.Connection.QuerySingleOrDefaultAsync<DocumentRelationshipRecord>(
            new CommandDefinition(sql, new { toDocumentId, relationshipCodeNorm }, uow.Transaction, cancellationToken: ct));
    }

    public async Task<bool> TryDeleteAsync(Guid relationshipId, CancellationToken ct = default)
    {
        relationshipId.EnsureRequired(nameof(relationshipId));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           DELETE FROM document_relationships
                           WHERE relationship_id = @relationshipId;
                           """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { relationshipId }, uow.Transaction, cancellationToken: ct));

        return rows == 1;
    }

    public async Task<IReadOnlyList<DocumentRelationshipRecord>> ListOutgoingAsync(
        Guid fromDocumentId,
        CancellationToken ct = default)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               relationship_id AS Id,
                               from_document_id AS FromDocumentId,
                               to_document_id AS ToDocumentId,
                               relationship_code AS RelationshipCode,
                               relationship_code_norm AS RelationshipCodeNorm,
                               created_at_utc AS CreatedAtUtc
                           FROM document_relationships
                           WHERE from_document_id = @fromDocumentId
                           ORDER BY created_at_utc DESC, relationship_id DESC;
                           """;

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipRecord>(
            new CommandDefinition(sql, new { fromDocumentId }, uow.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<DocumentRelationshipRecord>> ListIncomingAsync(
        Guid toDocumentId,
        CancellationToken ct = default)
    {
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               relationship_id AS Id,
                               from_document_id AS FromDocumentId,
                               to_document_id AS ToDocumentId,
                               relationship_code AS RelationshipCode,
                               relationship_code_norm AS RelationshipCodeNorm,
                               created_at_utc AS CreatedAtUtc
                           FROM document_relationships
                           WHERE to_document_id = @toDocumentId
                           ORDER BY created_at_utc DESC, relationship_id DESC;
                           """;

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipRecord>(
            new CommandDefinition(sql, new { toDocumentId }, uow.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<bool> ExistsPathAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCodeNorm,
        int maxDepth,
        CancellationToken ct = default)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        
        if (string.IsNullOrWhiteSpace(relationshipCodeNorm))
            throw new NgbArgumentRequiredException(nameof(relationshipCodeNorm));

        if (maxDepth <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "maxDepth must be > 0");

        // Read-only query (may run inside a transaction if the caller opened one).
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           WITH RECURSIVE walk(node_id, depth, path) AS (
                               SELECT @fromDocumentId::uuid AS node_id,
                                      0::int AS depth,
                                      ARRAY[@fromDocumentId::uuid]::uuid[] AS path
                               UNION ALL
                               SELECT r.to_document_id AS node_id,
                                      (w.depth + 1)::int AS depth,
                                      (w.path || r.to_document_id)::uuid[] AS path
                               FROM document_relationships r
                               JOIN walk w
                                 ON r.from_document_id = w.node_id
                               WHERE r.relationship_code_norm = @relationshipCodeNorm
                                 AND w.depth < @maxDepth
                                 AND NOT (r.to_document_id = ANY (w.path))
                           )
                           SELECT EXISTS(
                               SELECT 1
                               FROM walk
                               WHERE node_id = @toDocumentId::uuid
                           );
                           """;

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { fromDocumentId, toDocumentId, relationshipCodeNorm, maxDepth },
                uow.Transaction,
                cancellationToken: ct));
    }
}
