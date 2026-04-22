namespace NGB.Definitions.Documents.Relationships;

public enum DocumentRelationshipCardinality
{
    ManyToMany = 0,
    OneToMany = 1,
    ManyToOne = 2,
    OneToOne = 3
}

public sealed record DocumentRelationshipTypeDefinition(
    string Code,
    string Name,
    bool IsBidirectional,
    DocumentRelationshipCardinality Cardinality,
    IReadOnlyCollection<string>? AllowedFromTypeCodes,
    IReadOnlyCollection<string>? AllowedToTypeCodes)
{
    public int? MaxOutgoingPerFrom => Cardinality switch
    {
        DocumentRelationshipCardinality.ManyToOne => 1,
        DocumentRelationshipCardinality.OneToOne => 1,
        _ => null
    };

    public int? MaxIncomingPerTo => Cardinality switch
    {
        DocumentRelationshipCardinality.OneToMany => 1,
        DocumentRelationshipCardinality.OneToOne => 1,
        _ => null
    };
}
