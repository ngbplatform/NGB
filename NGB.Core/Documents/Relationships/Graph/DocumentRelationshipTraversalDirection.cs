namespace NGB.Core.Documents.Relationships.Graph;

[Flags]
public enum DocumentRelationshipTraversalDirection
{
    None = 0,
    Outgoing = 1,
    Incoming = 2,
    Both = Outgoing | Incoming
}
