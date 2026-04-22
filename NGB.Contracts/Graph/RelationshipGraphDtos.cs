using NGB.Contracts.Metadata;

namespace NGB.Contracts.Graph;

public sealed record GraphNodeDto(
    string NodeId,
    EntityKind Kind,
    string TypeCode,
    Guid EntityId,
    string Title,
    string? Subtitle = null,
    DocumentStatus? DocumentStatus = null,
    int Depth = 0,
    decimal? Amount = null);

public sealed record GraphEdgeDto(
    string FromNodeId,
    string ToNodeId,
    string RelationshipType,
    string? Label = null);

public sealed record RelationshipGraphDto(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);
