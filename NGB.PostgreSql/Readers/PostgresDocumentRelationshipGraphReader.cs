using Dapper;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Readers.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL implementation of <see cref="IDocumentRelationshipGraphReader"/>.
/// </summary>
public sealed class PostgresDocumentRelationshipGraphReader(IUnitOfWork uow) : IDocumentRelationshipGraphReader
{
    private const int MaxPageSize = 500;
    private const int MaxDepth = 5;
    private const int MaxCodeLength = 128;

    public async Task<DocumentRelationshipEdgePage> GetOutgoingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default)
    {
        ValidatePageRequest(request);
        var codeNorm = NormalizeOptionalCodeNorm(request.RelationshipCode);

        var limit = request.PageSize + 1;
        var afterCreated = request.Cursor?.AfterCreatedAtUtc;
        var afterId = request.Cursor?.AfterRelationshipId ?? Guid.Empty;

        const string sql = """
                           SELECT
                               r.relationship_id          AS "RelationshipId",
                               r.from_document_id         AS "FromDocumentId",
                               r.to_document_id           AS "ToDocumentId",
                               r.relationship_code         AS "RelationshipCode",
                               r.relationship_code_norm    AS "RelationshipCodeNorm",
                               r.created_at_utc            AS "CreatedAtUtc",
                               d.id                        AS "OtherDocumentId",
                               d.type_code                 AS "OtherTypeCode",
                               d.number                    AS "OtherNumber",
                               d.date_utc                  AS "OtherDateUtc",
                               d.status                    AS "OtherStatus"
                           FROM document_relationships r
                           JOIN documents d ON d.id = r.to_document_id
                           WHERE
                               r.from_document_id = @DocumentId::uuid
                               AND (@CodeNorm::text IS NULL OR r.relationship_code_norm = @CodeNorm::text)
                               AND (
                                   @AfterCreated::timestamptz IS NULL
                                   OR r.created_at_utc < @AfterCreated::timestamptz
                                   OR (r.created_at_utc = @AfterCreated::timestamptz AND r.relationship_id < @AfterId::uuid)
                               )
                           ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                           LIMIT @Limit;
                           """;

        var rows = (await uow.Connection.QueryAsync<EdgeRow>(
            new CommandDefinition(
                sql,
                new
                {
                    request.DocumentId,
                    CodeNorm = codeNorm,
                    AfterCreated = afterCreated,
                    AfterId = afterId,
                    Limit = limit
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var items = rows.Select(r => new DocumentRelationshipEdgeItem(
            r.RelationshipId,
            r.FromDocumentId,
            r.ToDocumentId,
            r.RelationshipCode,
            r.RelationshipCodeNorm,
            r.CreatedAtUtc,
            new DocumentRelationshipDocumentHeader(
                r.OtherDocumentId,
                r.OtherTypeCode,
                r.OtherNumber,
                r.OtherDateUtc,
                r.OtherStatus))).ToList();

        DocumentRelationshipEdgeCursor? next = null;
        
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            next = new DocumentRelationshipEdgeCursor(last.CreatedAtUtc, last.RelationshipId);
        }

        return new DocumentRelationshipEdgePage(items, hasMore, next);
    }

    public async Task<DocumentRelationshipEdgePage> GetIncomingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default)
    {
        ValidatePageRequest(request);
        var codeNorm = NormalizeOptionalCodeNorm(request.RelationshipCode);

        var limit = request.PageSize + 1;
        var afterCreated = request.Cursor?.AfterCreatedAtUtc;
        var afterId = request.Cursor?.AfterRelationshipId ?? Guid.Empty;

        const string sql = """
                           SELECT
                               r.relationship_id          AS "RelationshipId",
                               r.from_document_id         AS "FromDocumentId",
                               r.to_document_id           AS "ToDocumentId",
                               r.relationship_code         AS "RelationshipCode",
                               r.relationship_code_norm    AS "RelationshipCodeNorm",
                               r.created_at_utc            AS "CreatedAtUtc",
                               d.id                        AS "OtherDocumentId",
                               d.type_code                 AS "OtherTypeCode",
                               d.number                    AS "OtherNumber",
                               d.date_utc                  AS "OtherDateUtc",
                               d.status                    AS "OtherStatus"
                           FROM document_relationships r
                           JOIN documents d ON d.id = r.from_document_id
                           WHERE
                               r.to_document_id = @DocumentId::uuid
                               AND (@CodeNorm::text IS NULL OR r.relationship_code_norm = @CodeNorm::text)
                               AND (
                                   @AfterCreated::timestamptz IS NULL
                                   OR r.created_at_utc < @AfterCreated::timestamptz
                                   OR (r.created_at_utc = @AfterCreated::timestamptz AND r.relationship_id < @AfterId::uuid)
                               )
                           ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                           LIMIT @Limit;
                           """;

        var rows = (await uow.Connection.QueryAsync<EdgeRow>(
            new CommandDefinition(
                sql,
                new
                {
                    request.DocumentId,
                    CodeNorm = codeNorm,
                    AfterCreated = afterCreated,
                    AfterId = afterId,
                    Limit = limit
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var items = rows.Select(r => new DocumentRelationshipEdgeItem(
            r.RelationshipId,
            r.FromDocumentId,
            r.ToDocumentId,
            r.RelationshipCode,
            r.RelationshipCodeNorm,
            r.CreatedAtUtc,
            new DocumentRelationshipDocumentHeader(
                r.OtherDocumentId,
                r.OtherTypeCode,
                r.OtherNumber,
                r.OtherDateUtc,
                r.OtherStatus))).ToList();

        DocumentRelationshipEdgeCursor? next = null;
        
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            next = new DocumentRelationshipEdgeCursor(last.CreatedAtUtc, last.RelationshipId);
        }

        return new DocumentRelationshipEdgePage(items, hasMore, next);
    }

    public async Task<DocumentRelationshipGraph> GetGraphAsync(
        DocumentRelationshipGraphRequest request,
        CancellationToken ct = default)
    {
        if (request.RootDocumentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.RootDocumentId));

        if (request.MaxDepth < 0 || request.MaxDepth > MaxDepth)
            throw new NgbArgumentOutOfRangeException(nameof(request.MaxDepth), request.MaxDepth, $"MaxDepth must be between 0 and {MaxDepth}.");

        if (request.MaxNodes <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(request.MaxNodes), request.MaxNodes, "MaxNodes must be > 0.");

        if (request.MaxEdges <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(request.MaxEdges), request.MaxEdges, "MaxEdges must be > 0.");

        var codeNorms = NormalizeOptionalCodeNorms(request.RelationshipCodes);

        var visited = new HashSet<Guid> { request.RootDocumentId };
        var depthById = new Dictionary<Guid, int> { { request.RootDocumentId, 0 } };

        var edgeIds = new HashSet<Guid>();
        var edges = new List<DocumentRelationshipGraphEdge>();

        var frontier = new List<Guid> { request.RootDocumentId };
        for (var depth = 1; depth <= request.MaxDepth; depth++)
        {
            if (frontier.Count == 0)
                break;

            var remainingEdges = request.MaxEdges - edges.Count;
            if (remainingEdges <= 0)
                break;

            var nextFrontier = new List<Guid>();

            if (request.Direction.HasFlag(DocumentRelationshipTraversalDirection.Outgoing))
            {
                var outEdges = await QueryEdgesByFromIdsAsync(frontier, codeNorms, remainingEdges, ct);
                foreach (var e in outEdges)
                {
                    if (!edgeIds.Add(e.RelationshipId))
                        continue;

                    edges.Add(e);

                    if (visited.Count >= request.MaxNodes)
                        continue;

                    if (visited.Add(e.ToDocumentId))
                    {
                        depthById[e.ToDocumentId] = depth;
                        nextFrontier.Add(e.ToDocumentId);
                    }
                }
            }

            if (request.Direction.HasFlag(DocumentRelationshipTraversalDirection.Incoming))
            {
                var remainingAfterOutgoing = request.MaxEdges - edges.Count;
                var inEdges = await QueryEdgesByToIdsAsync(frontier, codeNorms, remainingAfterOutgoing, ct);
                
                foreach (var e in inEdges)
                {
                    if (!edgeIds.Add(e.RelationshipId))
                        continue;

                    edges.Add(e);

                    if (visited.Count >= request.MaxNodes)
                        continue;

                    if (visited.Add(e.FromDocumentId))
                    {
                        depthById[e.FromDocumentId] = depth;
                        nextFrontier.Add(e.FromDocumentId);
                    }
                }
            }

            frontier = nextFrontier;

            if (visited.Count >= request.MaxNodes || edges.Count >= request.MaxEdges)
                break;
        }

        // Invariant: never return edges that reference nodes not present in the node set.
        // With MaxNodes limiting traversal, we may have collected edges to neighbors that were not admitted.
        edges = edges
            .Where(e => visited.Contains(e.FromDocumentId) && visited.Contains(e.ToDocumentId))
            .ToList();

        var docs = await LoadDocumentHeadersAsync(visited, ct);
        if (!docs.ContainsKey(request.RootDocumentId))
            throw new DocumentNotFoundException(request.RootDocumentId);

        var nodes = docs.Values
            .Select(d => new DocumentRelationshipGraphNode(
                d.DocumentId,
                d.TypeCode,
                d.Number,
                d.DateUtc,
                d.Status,
                depthById.GetValueOrDefault(d.DocumentId, 0)))
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.DocumentId)
            .ToList();

        var orderedEdges = edges
            .OrderByDescending(e => e.CreatedAtUtc)
            .ThenByDescending(e => e.RelationshipId)
            .ToList();

        return new DocumentRelationshipGraph(request.RootDocumentId, nodes, orderedEdges);
    }

    private static void ValidatePageRequest(DocumentRelationshipEdgePageRequest request)
    {
        if (request.DocumentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.DocumentId));

        if (request.PageSize <= 0 || request.PageSize > MaxPageSize)
            throw new NgbArgumentOutOfRangeException(nameof(request.PageSize), request.PageSize, $"PageSize must be between 1 and {MaxPageSize}.");
    }

    private static string? NormalizeOptionalCodeNorm(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        if (trimmed.Length > MaxCodeLength)
            throw new NgbArgumentInvalidException(nameof(DocumentRelationshipEdgePageRequest.RelationshipCode), $"relationshipCode exceeds max length {MaxCodeLength}.");

        return trimmed.ToLowerInvariant();
    }

    private static string[]? NormalizeOptionalCodeNorms(IReadOnlyCollection<string>? codes)
    {
        if (codes is null || codes.Count == 0)
            return null;

        var norm = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in codes)
        {
            if (string.IsNullOrWhiteSpace(c))
                continue;

            var trimmed = c.Trim();
            if (trimmed.Length > MaxCodeLength)
                throw new NgbArgumentInvalidException(nameof(DocumentRelationshipGraphRequest.RelationshipCodes), $"relationshipCode exceeds max length {MaxCodeLength}.");

            norm.Add(trimmed.ToLowerInvariant());
        }

        return norm.Count == 0 ? null : norm.ToArray();
    }

    private async Task<IReadOnlyList<DocumentRelationshipGraphEdge>> QueryEdgesByFromIdsAsync(
        IReadOnlyList<Guid> fromIds,
        string[]? codeNorms,
        int limit,
        CancellationToken ct)
    {
        if (fromIds.Count == 0 || limit <= 0)
            return [];

        const string sql = """
                           SELECT
                               r.relationship_id       AS "RelationshipId",
                               r.from_document_id      AS "FromDocumentId",
                               r.to_document_id        AS "ToDocumentId",
                               r.relationship_code      AS "RelationshipCode",
                               r.relationship_code_norm AS "RelationshipCodeNorm",
                               r.created_at_utc         AS "CreatedAtUtc"
                           FROM document_relationships r
                           WHERE
                               r.from_document_id = ANY(@FromIds)
                               AND (@CodeNorms::text[] IS NULL OR r.relationship_code_norm = ANY(@CodeNorms))
                           ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                           LIMIT @Limit;
                           """;

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipGraphEdge>(
            new CommandDefinition(
                sql,
                new { FromIds = fromIds.ToArray(), CodeNorms = codeNorms, Limit = limit },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    private async Task<IReadOnlyList<DocumentRelationshipGraphEdge>> QueryEdgesByToIdsAsync(
        IReadOnlyList<Guid> toIds,
        string[]? codeNorms,
        int limit,
        CancellationToken ct)
    {
        if (toIds.Count == 0 || limit <= 0)
            return [];

        const string sql = """
                           SELECT
                               r.relationship_id       AS "RelationshipId",
                               r.from_document_id      AS "FromDocumentId",
                               r.to_document_id        AS "ToDocumentId",
                               r.relationship_code      AS "RelationshipCode",
                               r.relationship_code_norm AS "RelationshipCodeNorm",
                               r.created_at_utc         AS "CreatedAtUtc"
                           FROM document_relationships r
                           WHERE
                               r.to_document_id = ANY(@ToIds)
                               AND (@CodeNorms::text[] IS NULL OR r.relationship_code_norm = ANY(@CodeNorms))
                           ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                           LIMIT @Limit;
                           """;

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipGraphEdge>(
            new CommandDefinition(
                sql,
                new { ToIds = toIds.ToArray(), CodeNorms = codeNorms, Limit = limit },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    private async Task<Dictionary<Guid, DocumentRelationshipDocumentHeader>> LoadDocumentHeadersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, DocumentRelationshipDocumentHeader>();

        const string sql = """
                           SELECT
                               d.id        AS "DocumentId",
                               d.type_code AS "TypeCode",
                               d.number    AS "Number",
                               d.date_utc  AS "DateUtc",
                               d.status    AS "Status"
                           FROM documents d
                           WHERE d.id = ANY(@Ids);
                           """;

        var rows = await uow.Connection.QueryAsync<DocumentRelationshipDocumentHeader>(
            new CommandDefinition(
                sql,
                new { Ids = ids.ToArray() },
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(x => x.DocumentId, x => x);
    }

    private sealed class EdgeRow
    {
        public Guid RelationshipId { get; init; }
        public Guid FromDocumentId { get; init; }
        public Guid ToDocumentId { get; init; }
        public string RelationshipCode { get; init; } = string.Empty;
        public string RelationshipCodeNorm { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }

        public Guid OtherDocumentId { get; init; }
        public string OtherTypeCode { get; init; } = string.Empty;
        public string? OtherNumber { get; init; }
        public DateTime OtherDateUtc { get; init; }
        public DocumentStatus OtherStatus { get; init; }
    }
}
