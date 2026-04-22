using NGB.Tools.Exceptions;

namespace NGB.Definitions.Documents.Relationships;

public sealed class DocumentRelationshipTypeDefinitionBuilder
{
    private readonly DefinitionsBuilder.MutableDocumentRelationshipTypeDefinition _mutable;

    internal DocumentRelationshipTypeDefinitionBuilder(DefinitionsBuilder.MutableDocumentRelationshipTypeDefinition mutable)
        => _mutable = mutable;

    public DocumentRelationshipTypeDefinitionBuilder Name(string name)
    {
        _mutable.Name = name;
        return this;
    }

    public DocumentRelationshipTypeDefinitionBuilder Bidirectional(bool isBidirectional = true)
    {
        _mutable.IsBidirectional = isBidirectional;
        return this;
    }

    public DocumentRelationshipTypeDefinitionBuilder Cardinality(DocumentRelationshipCardinality cardinality)
    {
        _mutable.Cardinality = cardinality;
        return this;
    }

    public DocumentRelationshipTypeDefinitionBuilder ManyToMany()
        => Cardinality(DocumentRelationshipCardinality.ManyToMany);
    
    public DocumentRelationshipTypeDefinitionBuilder OneToMany()
        => Cardinality(DocumentRelationshipCardinality.OneToMany);
    
    public DocumentRelationshipTypeDefinitionBuilder ManyToOne()
        => Cardinality(DocumentRelationshipCardinality.ManyToOne);
    
    public DocumentRelationshipTypeDefinitionBuilder OneToOne()
        => Cardinality(DocumentRelationshipCardinality.OneToOne);

    public DocumentRelationshipTypeDefinitionBuilder AllowFromDocumentTypes(params string[] typeCodes)
    {
        if (typeCodes is null)
            throw new NgbArgumentRequiredException(nameof(typeCodes));

        foreach (var code in typeCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new NgbArgumentInvalidException(nameof(typeCodes), "TypeCode must be non-empty.");

            _mutable.AllowedFromTypeCodes.Add(code.Trim());
        }

        return this;
    }

    public DocumentRelationshipTypeDefinitionBuilder AllowToDocumentTypes(params string[] typeCodes)
    {
        if (typeCodes is null)
            throw new NgbArgumentRequiredException(nameof(typeCodes));

        foreach (var code in typeCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new NgbArgumentInvalidException(nameof(typeCodes), "TypeCode must be non-empty.");

            _mutable.AllowedToTypeCodes.Add(code.Trim());
        }

        return this;
    }
}
