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

    public async Task<IReadOnlyList<Guid>> TryCreateManyAsync(
        IReadOnlyList<DocumentRelationshipRecord> relationships,
        CancellationToken ct = default)
    {
        if (relationships is null)
            throw new NgbArgumentRequiredException(nameof(relationships));

        if (relationships.Count == 0)
            return [];

        if (relationships.Any(static relationship => relationship is null))
            throw new NgbArgumentInvalidException(nameof(relationships), "Relationship batch must not contain null items.");

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
INSERT INTO document_relationships
    (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
SELECT relationship_id,
       from_document_id,
       to_document_id,
       relationship_code,
       created_at_utc
  FROM UNNEST(
      @RelationshipIds::uuid[],
      @FromDocumentIds::uuid[],
      @ToDocumentIds::uuid[],
      @RelationshipCodes::text[],
      @CreatedAtUtc::timestamptz[]
  ) AS requested(
      relationship_id,
      from_document_id,
      to_document_id,
      relationship_code,
      created_at_utc
  )
ON CONFLICT (relationship_id) DO NOTHING
RETURNING relationship_id;
""";

        var ids = await uow.Connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new
            {
                RelationshipIds = relationships.Select(static relationship => relationship.Id).ToArray(),
                FromDocumentIds = relationships.Select(static relationship => relationship.FromDocumentId).ToArray(),
                ToDocumentIds = relationships.Select(static relationship => relationship.ToDocumentId).ToArray(),
                RelationshipCodes = relationships.Select(static relationship => relationship.RelationshipCode).ToArray(),
                CreatedAtUtc = relationships.Select(static relationship => relationship.CreatedAtUtc).ToArray()
            },
            uow.Transaction,
            cancellationToken: ct));

        return ids.AsList();
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

    public async Task<IReadOnlyList<Guid>> FindTargetsWithPathToAsync(
        Guid toDocumentId,
        IReadOnlyCollection<Guid> fromDocumentIds,
        string relationshipCodeNorm,
        int maxDepth,
        CancellationToken ct = default)
    {
        toDocumentId.EnsureRequired(nameof(toDocumentId));
        
        ArgumentNullException.ThrowIfNull(fromDocumentIds);

        if (string.IsNullOrWhiteSpace(relationshipCodeNorm))
            throw new NgbArgumentRequiredException(nameof(relationshipCodeNorm));

        if (maxDepth <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "maxDepth must be > 0");

        var sourceIds = fromDocumentIds.Distinct().ToArray();
        if (sourceIds.Length == 0)
            return [];

        if (sourceIds.Any(static id => id == Guid.Empty))
            throw new NgbArgumentInvalidException(nameof(fromDocumentIds), "Source ids must not contain an empty identifier.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH RECURSIVE walk(root_id, node_id, depth, path) AS (
    SELECT source_id,
           source_id,
           0,
           ARRAY[source_id]::uuid[]
      FROM UNNEST(@FromDocumentIds::uuid[]) AS requested(source_id)

    UNION ALL

    SELECT walk.root_id,
           relationship.to_document_id,
           walk.depth + 1,
           walk.path || relationship.to_document_id
      FROM walk
      JOIN document_relationships relationship
        ON relationship.from_document_id = walk.node_id
       AND relationship.relationship_code_norm = @RelationshipCodeNorm
     WHERE walk.depth < @MaxDepth
       AND NOT relationship.to_document_id = ANY(walk.path)
)
SELECT DISTINCT root_id
  FROM walk
 WHERE node_id = @ToDocumentId
 ORDER BY root_id;
""";

        var ids = await uow.Connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new
            {
                ToDocumentId = toDocumentId,
                FromDocumentIds = sourceIds,
                RelationshipCodeNorm = relationshipCodeNorm,
                MaxDepth = maxDepth
            },
            uow.Transaction,
            cancellationToken: ct));

        return ids.AsList();
    }

    public async Task<IReadOnlyList<int>> FindCycleCreatingRequestIndexesAsync(
        IReadOnlyList<DocumentRelationshipCycleCheck> checks,
        int maxDepth,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (maxDepth <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "maxDepth must be > 0");

        if (checks.Count == 0)
            return [];

        if (checks.Any(static check => check is null
            || check.FromDocumentId == Guid.Empty
            || check.ToDocumentId == Guid.Empty
            || string.IsNullOrWhiteSpace(check.RelationshipCodeNorm)))
        {
            throw new NgbArgumentInvalidException(nameof(checks), "Cycle checks must contain valid document ids and relationship codes.");
        }

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH RECURSIVE requested AS (
    SELECT request_index, from_document_id, to_document_id, relationship_code_norm
    FROM UNNEST(
        @RequestIndexes::integer[],
        @FromDocumentIds::uuid[],
        @ToDocumentIds::uuid[],
        @RelationshipCodeNorms::text[]
    ) AS item(request_index, from_document_id, to_document_id, relationship_code_norm)
),
walk(request_index, target_id, relationship_code_norm, node_id, depth, path) AS (
    SELECT request_index,
           from_document_id,
           relationship_code_norm,
           to_document_id,
           0,
           ARRAY[to_document_id]::uuid[]
    FROM requested

    UNION ALL

    SELECT walk.request_index,
           walk.target_id,
           walk.relationship_code_norm,
           relationship.to_document_id,
           walk.depth + 1,
           walk.path || relationship.to_document_id
    FROM walk
    JOIN document_relationships relationship
      ON relationship.from_document_id = walk.node_id
     AND relationship.relationship_code_norm = walk.relationship_code_norm
    WHERE walk.depth < @MaxDepth
      AND NOT relationship.to_document_id = ANY(walk.path)
)
SELECT DISTINCT request_index
FROM walk
WHERE node_id = target_id
ORDER BY request_index;
""";

        var indexes = await uow.Connection.QueryAsync<int>(new CommandDefinition(
            sql,
            new
            {
                RequestIndexes = Enumerable.Range(0, checks.Count).ToArray(),
                FromDocumentIds = checks.Select(static check => check.FromDocumentId).ToArray(),
                ToDocumentIds = checks.Select(static check => check.ToDocumentId).ToArray(),
                RelationshipCodeNorms = checks.Select(static check => check.RelationshipCodeNorm).ToArray(),
                MaxDepth = maxDepth
            },
            uow.Transaction,
            cancellationToken: ct));

        return indexes.AsList();
    }

    public async Task<IReadOnlyList<DocumentRelationshipRecord>> GetCardinalityConflictsAsync(
        IReadOnlyList<DocumentRelationshipCardinalityCheck> checks,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (checks.Count == 0)
            return [];

        if (checks.Any(static check => check is null
            || check.FromDocumentId == Guid.Empty
            || check.ToDocumentId == Guid.Empty
            || string.IsNullOrWhiteSpace(check.RelationshipCodeNorm)
            || check is { CheckOutgoing: false, CheckIncoming: false }))
        {
            throw new NgbArgumentInvalidException(nameof(checks), "Cardinality checks must contain valid ids, codes, and at least one direction.");
        }

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH requested AS (
    SELECT from_document_id,
           to_document_id,
           relationship_code_norm,
           check_outgoing,
           check_incoming
    FROM UNNEST(
        @FromDocumentIds::uuid[],
        @ToDocumentIds::uuid[],
        @RelationshipCodeNorms::text[],
        @CheckOutgoing::boolean[],
        @CheckIncoming::boolean[]
    ) AS item(from_document_id, to_document_id, relationship_code_norm, check_outgoing, check_incoming)
)
SELECT DISTINCT
    relationship.relationship_id AS Id,
    relationship.from_document_id AS FromDocumentId,
    relationship.to_document_id AS ToDocumentId,
    relationship.relationship_code AS RelationshipCode,
    relationship.relationship_code_norm AS RelationshipCodeNorm,
    relationship.created_at_utc AS CreatedAtUtc
FROM document_relationships relationship
JOIN requested
  ON requested.relationship_code_norm = relationship.relationship_code_norm
 AND ((requested.check_outgoing AND requested.from_document_id = relationship.from_document_id)
      OR (requested.check_incoming AND requested.to_document_id = relationship.to_document_id));
""";

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipRecord>(new CommandDefinition(
            sql,
            new
            {
                FromDocumentIds = checks.Select(static check => check.FromDocumentId).ToArray(),
                ToDocumentIds = checks.Select(static check => check.ToDocumentId).ToArray(),
                RelationshipCodeNorms = checks.Select(static check => check.RelationshipCodeNorm).ToArray(),
                CheckOutgoing = checks.Select(static check => check.CheckOutgoing).ToArray(),
                CheckIncoming = checks.Select(static check => check.CheckIncoming).ToArray()
            },
            uow.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }
}
