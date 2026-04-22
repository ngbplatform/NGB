using NGB.Definitions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Platform-level relationship types for Document Relationships.
/// Modules can extend this set using Definitions.
/// </summary>
public sealed class DocumentRelationshipsDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        // Generic / ad-hoc relationship used by tests and many business flows.
        builder.AddDocumentRelationshipType(
            relationshipCode: "based_on",
            configure: r => r
                .Name("Based on")
                .ManyToMany()
                .Bidirectional(false));

        // Symmetric, user-facing relationship.
        builder.AddDocumentRelationshipType(
            relationshipCode: "related_to",
            configure: r => r
                .Name("Related to")
                .ManyToMany()
                .Bidirectional(true));

        // Strict business relationships with cardinality.
        builder.AddDocumentRelationshipType(
            relationshipCode: "reversal_of",
            configure: r => r
                .Name("Reversal of")
                .ManyToOne()
                .Bidirectional(false));

        builder.AddDocumentRelationshipType(
            relationshipCode: "created_from",
            configure: r => r
                .Name("Created from")
                .ManyToOne()
                .Bidirectional(false));

        builder.AddDocumentRelationshipType(
            relationshipCode: "supersedes",
            configure: r => r
                .Name("Supersedes")
                .OneToOne()
                .Bidirectional(false));
    }
}
