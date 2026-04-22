using NGB.Tools.Exceptions;

namespace NGB.Definitions.Documents.Derivations;

public sealed class DocumentDerivationDefinitionBuilder
{
    private readonly DefinitionsBuilder.MutableDocumentDerivationDefinition _mutable;

    internal DocumentDerivationDefinitionBuilder(DefinitionsBuilder.MutableDocumentDerivationDefinition mutable)
        => _mutable = mutable;

    public DocumentDerivationDefinitionBuilder Name(string name)
    {
        _mutable.Name = name;
        return this;
    }

    public DocumentDerivationDefinitionBuilder From(string sourceTypeCode)
    {
        _mutable.FromTypeCode = sourceTypeCode;
        return this;
    }

    public DocumentDerivationDefinitionBuilder To(string targetTypeCode)
    {
        _mutable.ToTypeCode = targetTypeCode;
        return this;
    }

    /// <summary>
    /// Adds a relationship code to be created from the derived draft to the source document.
    ///
    /// Recommended codes for "Enter based on":
    /// - created_from
    /// - based_on
    /// </summary>
    public DocumentDerivationDefinitionBuilder Relationship(string relationshipCode)
    {
        _mutable.AddRelationship(relationshipCode);
        return this;
    }

    public DocumentDerivationDefinitionBuilder Relationships(params string[] relationshipCodes)
    {
        if (relationshipCodes is null)
            throw new NgbArgumentRequiredException(nameof(relationshipCodes));

        foreach (var c in relationshipCodes)
            _mutable.AddRelationship(c);

        return this;
    }

    /// <summary>
    /// Optional handler that can prefill the derived draft.
    ///
    /// The handler is invoked inside the same transaction as draft creation and relationship writes.
    /// </summary>
    public DocumentDerivationDefinitionBuilder Handler(Type handlerType)
    {
        _mutable.HandlerType = handlerType;
        return this;
    }

    public DocumentDerivationDefinitionBuilder Handler<THandler>() where THandler : class
        => Handler(typeof(THandler));
}
