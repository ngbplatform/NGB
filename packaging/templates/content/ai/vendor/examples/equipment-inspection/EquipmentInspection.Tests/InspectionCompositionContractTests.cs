using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Definitions;
using NGB.Definitions.Documents.Relationships;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class InspectionCompositionContractTests
{
    [Fact]
    public void Extension_preserves_document_metadata_and_refuses_conflicting_bindings()
    {
        var builder = new DefinitionsBuilder();
        new EquipmentDefinitions().Contribute(builder);
        var original = builder.Build().GetDocument(EquipmentDefinitions.InspectionCode);
        new InspectionRuntimeDefinitions().Contribute(builder);
        builder.ExtendDocument(EquipmentDefinitions.InspectionCode,
            document => document.AddPostValidator<InspectionPostValidator>());
        var extended = builder.Build().GetDocument(EquipmentDefinitions.InspectionCode);
        Assert.Same(original.Metadata, extended.Metadata);
        Assert.Equal(original.TypeCode, extended.TypeCode);
        Assert.Equal(typeof(InspectionPostValidator), Assert.Single(extended.PostValidatorTypes));
        Assert.Equal(typeof(InspectionNumberingPolicy), extended.NumberingPolicyType);
        Assert.Throws<NgbConfigurationViolationException>(() => builder.ExtendDocument(EquipmentDefinitions.InspectionCode,
            document => document.NumberingPolicy<object>()));
        Assert.Throws<NgbConfigurationViolationException>(() => builder.ExtendDocument(EquipmentDefinitions.InspectionCode,
            document => document.Metadata(original.Metadata)));
        Assert.Throws<NgbConfigurationViolationException>(() => builder.ExtendDocument("example.missing", _ => { }));
        var numbering = new InspectionNumberingPolicy();
        Assert.Equal(EquipmentDefinitions.InspectionCode, numbering.TypeCode);
        Assert.True(numbering.EnsureNumberOnCreateDraft);
        Assert.False(numbering.EnsureNumberOnPost);
    }

    [Fact]
    public void Derivation_direction_cardinality_and_identity_are_explicit_and_duplicate_definitions_are_rejected()
    {
        var builder = new DefinitionsBuilder();
        new EquipmentDefinitions().Contribute(builder);
        new InspectionDerivationDefinitions().Contribute(builder);
        var registry = builder.Build();
        var relationship = registry.GetDocumentRelationshipType(InspectionDerivationDefinitions.RelationshipCode);
        Assert.Equal(DocumentRelationshipCardinality.ManyToOne, relationship.Cardinality);
        Assert.False(relationship.IsBidirectional);
        Assert.Equal(1, relationship.MaxOutgoingPerFrom);
        Assert.Null(relationship.MaxIncomingPerTo);
        Assert.Equal(EquipmentDefinitions.InspectionCode, Assert.Single(relationship.AllowedFromTypeCodes!));
        Assert.Equal(EquipmentDefinitions.InspectionCode, Assert.Single(relationship.AllowedToTypeCodes!));
        var derivation = registry.GetDocumentDerivation(InspectionDerivationDefinitions.ActionCode);
        Assert.Equal(EquipmentDefinitions.InspectionCode, derivation.FromTypeCode);
        Assert.Equal(EquipmentDefinitions.InspectionCode, derivation.ToTypeCode);
        Assert.Equal(relationship.Code, Assert.Single(derivation.RelationshipCodes));
        Assert.Null(derivation.HandlerType);
        Assert.Throws<NgbConfigurationViolationException>(() => new InspectionDerivationDefinitions().Contribute(builder));
        Assert.Throws<NgbConfigurationViolationException>(() =>
            builder.AddDocumentDerivation(InspectionDerivationDefinitions.ActionCode, _ => { }));

        builder.ExtendDocumentRelationshipType(relationship.Code, item => item.Name("Original inspection"));
        builder.ExtendDocumentDerivation(derivation.Code, item => item.Name("Schedule reinspection"));
        var extended = builder.Build();
        var extendedRelationship = extended.GetDocumentRelationshipType(relationship.Code);
        Assert.Equal("Original inspection", extendedRelationship.Name);
        Assert.Equal(relationship.Cardinality, extendedRelationship.Cardinality);
        Assert.Equal(relationship.AllowedFromTypeCodes, extendedRelationship.AllowedFromTypeCodes);
        Assert.Equal(relationship.AllowedToTypeCodes, extendedRelationship.AllowedToTypeCodes);
        var extendedDerivation = extended.GetDocumentDerivation(derivation.Code);
        Assert.Equal("Schedule reinspection", extendedDerivation.Name);
        Assert.Equal(derivation.FromTypeCode, extendedDerivation.FromTypeCode);
        Assert.Equal(derivation.ToTypeCode, extendedDerivation.ToTypeCode);
        Assert.Equal(derivation.RelationshipCodes, extendedDerivation.RelationshipCodes);
        Assert.Throws<NgbConfigurationViolationException>(() =>
            builder.ExtendDocumentRelationshipType("example.missing", _ => { }));
        Assert.Throws<NgbConfigurationViolationException>(() =>
            builder.ExtendDocumentDerivation("example.missing", _ => { }));
    }
}
