using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Relationships;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Documents.Derivations;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// These tests intentionally exercise branches in <see cref="NGB.Runtime.Definitions.Validation.DefinitionsValidationService"/>
/// that are hard to reach through the normal DI + hosted-service startup path.
///
/// Goals:
/// - cover "IServiceProviderIsService is not available" branch
/// - cover multi-category error aggregation (documents + catalogs + relationship types + derivations)
/// - keep everything platform-level (no Demo.Trade)
/// </summary>
public sealed class DefinitionsValidationService_InternalBranchCoverage_P0Tests
{
    [Fact]
    public void ValidateOrThrow_WhenIServiceProviderIsServiceNotAvailable_ReportsCannotValidateDIRegistration()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    typeCode: "doc.di.probe",
                    metadata: NewDocMetadata("doc.di.probe"),
                    typedStorageType: typeof(OkDocStorage))
            ]);

        var validator = CreateInternalValidator(registry, isService: null);

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;
        ex.Errors.Should().ContainSingle(e => e.IndexOf("cannot validate DI registration", StringComparison.OrdinalIgnoreCase) >= 0);
        ex.Errors[0].Should().Contain("Document 'doc.di.probe'");
        ex.Errors[0].Should().Contain("TypedStorageType");
    }

    [Fact]
    public void ValidateOrThrow_AccumulatesErrorsAcrossAllDefinitionCategories()
    {
        // NOTE: We bypass NgbDefinitionsBuilder normalization by constructing the registry manually.
        // This allows us to cover code-length / trimming checks that are otherwise prevented by the builder.

        var doc = new DocumentTypeDefinition(
            typeCode: "doc.agg",
            metadata: NewDocMetadata("doc.agg.other"),
            typedStorageType: typeof(IDocumentTypeStorage), // interface => must be concrete
            postingHandlerType: typeof(OpenGenericPostingHandler<>), // open generic
            numberingPolicyType: typeof(NotANumberingPolicy), // wrong interface
            approvalPolicyType: typeof(AbstractApprovalPolicy), // abstract
            draftValidatorTypes: [typeof(NotADraftValidator)], // wrong interface
            postValidatorTypes: [typeof(OpenGenericPostValidator<>)]); // open generic

        var cat = new CatalogTypeDefinition(
            typeCode: "cat.agg",
            metadata: NewCatMetadata("cat.agg.other"),
            typedStorageType: typeof(WrongCatalogStorage)); // wrong interface

        var rel1 = new DocumentRelationshipTypeDefinition(
            Code: "  rel.agg  ", // not trimmed
            Name: "", // empty
            IsBidirectional: true,
            Cardinality: DocumentRelationshipCardinality.ManyToMany,
            AllowedFromTypeCodes: ["doc.unknown"], // unknown
            AllowedToTypeCodes: ["doc.agg"]);

        var rel2 = new DocumentRelationshipTypeDefinition(
            Code: new string('a', 129), // exceeds max length 128
            Name: "ok",
            IsBidirectional: false,
            Cardinality: DocumentRelationshipCardinality.ManyToMany,
            AllowedFromTypeCodes: null,
            AllowedToTypeCodes: null);

        var deriv = new DocumentDerivationDefinition(
            Code: "  ",
            Name: "",
            FromTypeCode: "",
            ToTypeCode: "doc.unknown",
            RelationshipCodes: new List<string> { "", "rel.unknown", "  rel.agg  " },
            HandlerType: typeof(AbstractDerivationHandler));

        var registry = new DefinitionsRegistry(
            documents: new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [doc.TypeCode] = doc
            },
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [cat.TypeCode] = cat
            },
            documentRelationshipTypes: new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                // IMPORTANT: key is the untrimmed code, so TryGetDocumentRelationshipType(relCode.Trim()) will not find it.
                [rel1.Code] = rel1,
                [rel2.Code] = rel2
            },
            documentDerivations: new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["deriv.agg"] = deriv
            });

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;

        // The exact count matters here: it guards against accidental early returns and missing category validation.
        ex.Errors.Should().HaveCount(23);
        ex.Message.Should().Contain("Definitions validation failed: 23 error(s).");

        ex.Errors.Should().Contain(e => e.IndexOf("Document 'doc.agg': Metadata.TypeCode does not match", StringComparison.Ordinal) >= 0);
        ex.Errors.Should().Contain(e => e.IndexOf("Catalog 'cat.agg': Metadata.CatalogCode does not match", StringComparison.Ordinal) >= 0);
        ex.Errors.Should().Contain(e => e.IndexOf("DocumentRelationshipType: Code must be a non-empty trimmed string", StringComparison.Ordinal) >= 0);
        ex.Errors.Should().Contain(e => e.IndexOf("DocumentDerivation: Code must be a non-empty trimmed string", StringComparison.Ordinal) >= 0);

        // Also ensure we hit the 'Trim() lookup' path for derivation relationship codes.
        ex.Errors.Should().Contain(e => e.IndexOf("RelationshipCodes references unknown relationship type 'rel.agg'", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void ValidateOrThrow_WhenIsServiceReturnsFalse_AddsNotRegisteredInDIError()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    typeCode: "doc.di.missing",
                    metadata: NewDocMetadata("doc.di.missing"),
                    typedStorageType: typeof(OkDocStorage))
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysFalseIsService());

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;
        ex.Errors.Should().ContainSingle(e => e.IndexOf("is not registered in DI", StringComparison.Ordinal) >= 0);
        ex.Errors[0].Should().Contain(nameof(OkDocStorage), nameof(StringComparison.Ordinal));
    }

    // --- Helpers ---

    private static DefinitionsRegistry BuildRegistry(
        IEnumerable<DocumentTypeDefinition>? documents = null,
        IEnumerable<CatalogTypeDefinition>? catalogs = null,
        IEnumerable<DocumentRelationshipTypeDefinition>? relationships = null,
        IEnumerable<DocumentDerivationDefinition>? derivations = null)
    {
        var docs = (documents ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var cats = (catalogs ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var rels = (relationships ?? [])
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var ders = (derivations ?? [])
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        return new DefinitionsRegistry(docs, cats, rels, ders);
    }

    private static DocumentTypeMetadata NewDocMetadata(string typeCode)
        => new(typeCode, Tables: [], Presentation: new DocumentPresentationMetadata("Test"));

    private static CatalogTypeMetadata NewCatMetadata(string catalogCode)
        => new(
            CatalogCode: catalogCode,
            DisplayName: "Test",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata("cat_test", "name"),
            Version: new CatalogMetadataVersion(1, "tests"));

    private static IDefinitionsValidationService CreateInternalValidator(
        DefinitionsRegistry registry,
        IServiceProviderIsService? isService)
    {
        var runtimeAsm = typeof(IDefinitionsValidationService).Assembly;
        var impl = runtimeAsm.GetType("NGB.Runtime.Definitions.Validation.DefinitionsValidationService", throwOnError: true)!;

        // NOTE: Activator.CreateInstance(...) is surprisingly fragile with non-public primary constructors.
        // We locate the ctor explicitly to avoid MissingMethodException on some runtimes.
        var ctors = impl.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // primary ctor: (DefinitionsRegistry registry, IServiceProviderIsService? isService = null)
        var ctor = ctors.SingleOrDefault(c =>
        {
            var ps = c.GetParameters();
            return ps.Length == 2
                   && ps[0].ParameterType == typeof(DefinitionsRegistry)
                   && ps[1].ParameterType == typeof(IServiceProviderIsService);
        });

        ctor.Should().NotBeNull($"Expected an internal ctor (DefinitionsRegistry, IServiceProviderIsService) on {impl.FullName}, but found: " +
                               string.Join(", ", ctors.Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")")));

        var instance = ctor!.Invoke([registry, isService]);
        instance.Should().NotBeNull("the internal validator type must be constructible via reflection");

        return (IDefinitionsValidationService)instance;
    }

    // --- Test binding types ---

    private sealed class OkDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "doc.di";

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class WrongCatalogStorage
    {
    }

    private sealed class NotANumberingPolicy
    {
    }

    private abstract class AbstractApprovalPolicy : IDocumentApprovalPolicy
    {
        public string TypeCode => "doc.agg";

        public Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NotADraftValidator
    {
    }

    private sealed class OpenGenericPostValidator<T> : IDocumentPostValidator
    {
        public string TypeCode => "doc.agg";

        public Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class OpenGenericPostingHandler<T> : IDocumentPostingHandler
    {
        public string TypeCode => "doc.agg";

        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
            => Task.CompletedTask;
    }

    private abstract class AbstractDerivationHandler : IDocumentDerivationHandler
    {
        public abstract Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default);
    }

    private sealed class AlwaysTrueIsService : IServiceProviderIsService
    {
        public bool IsService(Type serviceType) => true;
    }

    private sealed class AlwaysFalseIsService : IServiceProviderIsService
    {
        public bool IsService(Type serviceType) => false;
    }
}
