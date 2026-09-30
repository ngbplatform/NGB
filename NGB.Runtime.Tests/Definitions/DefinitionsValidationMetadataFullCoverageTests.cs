using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Relationships;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Documents.Derivations;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Definitions;

public sealed class DefinitionsValidationMetadataFullCoverageTests
{
    [Fact]
    public async Task ConstructorRejectsNullAndEmptyRegistryPassesWithoutScopeFactory()
    {
        ((Action)(() => new DefinitionsValidationService(null!))).Should().Throw<NgbArgumentRequiredException>();
        await new DefinitionsValidationService(Registry()).ValidateOrThrowAsync();
    }

    [Fact]
    public async Task DocumentsValidateMetadataAmountPartsAndEveryBindingShape()
    {
        var validNumberingProxy = Moq.Mock.Of<NGB.Definitions.Documents.Numbering.IDocumentNumberingPolicy>();
        var documents = new List<DocumentTypeDefinition>
        {
            Doc("meta_mismatch", metadataCode: "other"),
            Doc("amount_blank", presentation: new DocumentPresentationMetadata(AmountField: " ")),
            Doc("amount_untrimmed", presentation: new DocumentPresentationMetadata(AmountField: " amount ")),
            Doc("amount_no_head", [Part("part", "items")], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("amount_missing", [Head("head", Col("other", ColumnType.Decimal))], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("amount_wrong_type", [Head("head", Col("amount", ColumnType.String))], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("amount_decimal", [Head("head", Col("amount", ColumnType.Decimal))], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("amount_int", [Head("head", Col("amount", ColumnType.Int32))], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("amount_long", [Head("head", Col("amount", ColumnType.Int64))], new DocumentPresentationMetadata(AmountField: "amount")),
            Doc("parts", [
                Head("head_with_part", partCode: "forbidden"),
                Part("empty_part", null),
                Part("untrimmed_part", " items "),
                Part("items_a", "items"),
                Part("items_b", "ITEMS")
            ]),
            new DocumentTypeDefinition("bindings", new DocumentTypeMetadata("bindings", []),
                typedStorageType: typeof(NGB.Persistence.Documents.Storage.IDocumentTypeStorage),
                postingHandlerType: typeof(AbstractMarker),
                operationalRegisterPostingHandlerType: typeof(List<>),
                referenceRegisterPostingHandlerType: typeof(string),
                numberingPolicyType: validNumberingProxy.GetType(),
                approvalPolicyType: null,
                draftValidatorTypes: [typeof(string)],
                postValidatorTypes: [typeof(NGB.Definitions.Documents.Validation.IDocumentPostValidator)])
        };

        var errors = await ValidateAsync(Registry(documents: documents));

        errors.Should().Contain(x => x.Contains("Metadata.TypeCode"));
        errors.Should().Contain(x => x.Contains("AmountField") && x.Contains("trimmed"));
        errors.Should().Contain(x => x.Contains("no head table"));
        errors.Should().Contain(x => x.Contains("numeric head-table"));
        errors.Should().Contain(x => x.Contains("Decimal, Int32, or Int64"));
        errors.Should().Contain(x => x.Contains("cannot declare PartCode"));
        errors.Should().Contain(x => x.Contains("must declare a non-empty PartCode"));
        errors.Should().Contain(x => x.Contains("trimmed PartCode"));
        errors.Should().Contain(x => x.Contains("duplicate PartCode"));
        errors.Should().Contain(x => x.Contains("must be a concrete type"));
        errors.Should().Contain(x => x.Contains("closed constructed type"));
        errors.Should().Contain(x => x.Contains("must implement"));
        errors.Should().Contain(x => x.Contains("IServiceProviderIsService is not available"));
    }

    [Fact]
    public async Task MirroredRelationshipsValidateLocationSystemTypeCodeLookupAllowListsAndTargets()
    {
        var relationships = new[]
        {
            Rel("valid", allowedFrom: ["source"], allowedTo: ["target"]),
            Rel("open")
        };
        var columns = new[]
        {
            Mirror("part_field", ColumnType.String, " ", null),
            Mirror("document_id", ColumnType.Guid, "valid", new DocumentLookupSourceMetadata(["target"])),
            Mirror("not_guid", ColumnType.String, "valid", new DocumentLookupSourceMetadata(["target"])),
            Mirror("not_lookup", ColumnType.Guid, "valid", null),
            Mirror("unknown_rel", ColumnType.Guid, "missing", new DocumentLookupSourceMetadata(["target"])),
            Mirror("from_denied", ColumnType.Guid, "valid", new DocumentLookupSourceMetadata(["target"])),
            Mirror("unknown_target", ColumnType.Guid, "open", new DocumentLookupSourceMetadata(["missing_doc"])),
            Mirror("open_target", ColumnType.Guid, "open", new DocumentLookupSourceMetadata(["target"])),
            Mirror("target_denied", ColumnType.Guid, "valid", new DocumentLookupSourceMetadata(["source"])),
            Mirror("valid_target", ColumnType.Guid, "valid", new DocumentLookupSourceMetadata(["target"]))
        };
        var docs = new[]
        {
            Doc("source"),
            Doc("target"),
            Doc("mirrors", [new DocumentTableMetadata("part", TableKind.Part, [columns[0]], PartCode: "part"),
                Head("head", columns[1..])])
        };

        var errors = await ValidateAsync(Registry(docs, relationships: relationships));

        errors.Should().Contain(x => x.Contains("head-table column"));
        errors.Should().Contain(x => x.Contains("system column 'document_id'"));
        errors.Should().Contain(x => x.Contains("ColumnType.Guid"));
        errors.Should().Contain(x => x.Contains("non-empty trimmed relationship code"));
        errors.Should().Contain(x => x.Contains("document lookup field"));
        errors.Should().Contain(x => x.Contains("unknown relationship type"));
        errors.Should().Contain(x => x.Contains("does not allow from-document"));
        errors.Should().Contain(x => x.Contains("unknown target document"));
        errors.Should().Contain(x => x.Contains("does not allow target document"));
    }

    [Fact]
    public async Task CatalogsValidateMetadataPartsAndValidatorBindings()
    {
        var catalogs = new[]
        {
            Catalog("mismatch", metadataCode: "other"),
            Catalog("parts", [
                CatalogHead("head", partCode: "forbidden"),
                CatalogPart("empty", null),
                CatalogPart("untrimmed", " rows "),
                CatalogPart("one", "rows"),
                CatalogPart("two", "ROWS")
            ]),
            new CatalogTypeDefinition("bindings", CatalogMetadata("bindings", []),
                typedStorageType: typeof(NGB.Persistence.Catalogs.Storage.ICatalogTypeStorage),
                validatorTypes: [typeof(string), typeof(NGB.Definitions.Catalogs.Validation.ICatalogUpsertValidator)])
        };

        var errors = await ValidateAsync(Registry(catalogs: catalogs));

        errors.Should().Contain(x => x.Contains("Metadata.CatalogCode"));
        errors.Should().Contain(x => x.Contains("cannot declare PartCode"));
        errors.Should().Contain(x => x.Contains("must declare a non-empty PartCode"));
        errors.Should().Contain(x => x.Contains("trimmed PartCode"));
        errors.Should().Contain(x => x.Contains("duplicate PartCode"));
        errors.Should().Contain(x => x.Contains("must be a concrete type"));
        errors.Should().Contain(x => x.Contains("must implement"));
    }

    [Fact]
    public async Task RelationshipTypesValidateCodesNamesAllowedDocumentsAndBidirectionalSymmetry()
    {
        var docs = new[] { Doc("source"), Doc("target") };
        var longCode = new string('x', 129);
        var relationships = new[]
        {
            Rel(" "),
            Rel(" bad ", name: " "),
            Rel(longCode),
            Rel("empty_allowed", allowedFrom: ["", "missing"], allowedTo: ["target"]),
            Rel("asymmetric", bidirectional: true, allowedFrom: ["source"], allowedTo: ["target"]),
            Rel("symmetric", bidirectional: true, allowedFrom: ["source", "target"], allowedTo: ["TARGET", "SOURCE"]),
            Rel("open", bidirectional: true)
        };

        var errors = await ValidateAsync(Registry(docs, relationships: relationships));

        errors.Should().Contain(x => x.Contains("Code must be a non-empty trimmed"));
        errors.Should().Contain(x => x.Contains("exceeds max length 128"));
        errors.Should().Contain(x => x.Contains("Name must be non-empty"));
        errors.Should().Contain(x => x.Contains("empty TypeCode"));
        errors.Should().Contain(x => x.Contains("unknown document type"));
        errors.Should().Contain(x => x.Contains("Bidirectional relationship must have identical"));
    }

    [Fact]
    public async Task DerivationsValidateCodesEndpointsRelationshipsDuplicatesAndHandlerBindings()
    {
        var docs = new[] { Doc("source"), Doc("target") };
        var relationships = new[] { Rel("valid") };
        var longCode = new string('d', 129);
        var longRel = new string('r', 129);
        var derivations = new[]
        {
            Derivation(" ", "Blank", "source", "target", ["valid"]),
            Derivation(" bad ", " ", "", "", null!),
            Derivation(longCode, "Long", "missing_from", "missing_to", []),
            Derivation("relationships", "Relationships", "source", "target",
                ["", " valid ", longRel, "valid", "VALID", "missing"], typeof(string)),
            Derivation("valid", "Valid", "source", "target", ["valid"])
        };

        var errors = await ValidateAsync(Registry(docs, relationships: relationships, derivations: derivations));

        errors.Should().Contain(x => x.Contains("DocumentDerivation: Code"));
        errors.Should().Contain(x => x.Contains("Code exceeds max length"));
        errors.Should().Contain(x => x.Contains("Name must be non-empty"));
        errors.Should().Contain(x => x.Contains("FromTypeCode must be non-empty"));
        errors.Should().Contain(x => x.Contains("unknown document type"));
        errors.Should().Contain(x => x.Contains("RelationshipCodes must contain"));
        errors.Should().Contain(x => x.Contains("contains an empty code"));
        errors.Should().Contain(x => x.Contains("non-trimmed code"));
        errors.Should().Contain(x => x.Contains("exceeding max length"));
        errors.Should().Contain(x => x.Contains("duplicate code"));
        errors.Should().Contain(x => x.Contains("unknown relationship type"));
        errors.Should().Contain(x => x.Contains("must implement IDocumentDerivationHandler"));
    }

    [Fact]
    public async Task RuntimeBindingsAcceptEveryRegisteredBindingAndMatchingCode()
    {
        var services = new ServiceCollection();

        var documentStorage = MockWithCode<IDocumentTypeStorage>("bound", x => x.TypeCode);
        var posting = MockWithCode<IDocumentPostingHandler>("bound", x => x.TypeCode);
        var operationalPosting = MockWithCode<IDocumentOperationalRegisterPostingHandler>("bound", x => x.TypeCode);
        var referencePosting = MockWithCode<IDocumentReferenceRegisterPostingHandler>("bound", x => x.TypeCode);
        var numbering = MockWithCode<IDocumentNumberingPolicy>("bound", x => x.TypeCode);
        var approval = MockWithCode<IDocumentApprovalPolicy>("bound", x => x.TypeCode);
        var draftValidator = MockWithCode<IDocumentDraftValidator>("bound", x => x.TypeCode);
        var postValidator = MockWithCode<IDocumentPostValidator>("bound", x => x.TypeCode);
        var catalogStorage = MockWithCode<ICatalogTypeStorage>("catalog", x => x.CatalogCode);
        var catalogValidator = MockWithCode<ICatalogUpsertValidator>("catalog", x => x.TypeCode);
        var derivationHandler = new Mock<IDocumentDerivationHandler>();

        var documentStorageType = Register(services, documentStorage.Object);
        var postingType = Register(services, posting.Object);
        var operationalPostingType = Register(services, operationalPosting.Object);
        var referencePostingType = Register(services, referencePosting.Object);
        var numberingType = Register(services, numbering.Object);
        var approvalType = Register(services, approval.Object);
        var draftValidatorType = Register(services, draftValidator.Object);
        var postValidatorType = Register(services, postValidator.Object);
        var catalogStorageType = Register(services, catalogStorage.Object);
        var catalogValidatorType = Register(services, catalogValidator.Object);
        var derivationHandlerType = Register(services, derivationHandler.Object);

        using var provider = services.BuildServiceProvider();
        var document = new DocumentTypeDefinition(
            "bound",
            new DocumentTypeMetadata("bound", []),
            documentStorageType,
            postingType,
            operationalPostingType,
            referencePostingType,
            numberingType,
            approvalType,
            [draftValidatorType],
            [postValidatorType]);
        var catalog = new CatalogTypeDefinition(
            "catalog",
            CatalogMetadata("catalog", []),
            catalogStorageType,
            [catalogValidatorType]);
        var registry = Registry(
            [document],
            [catalog],
            [Rel("derived")],
            [Derivation("derive", "Derive", "bound", "bound", ["derived"], derivationHandlerType)]);

        await CreateRuntimeValidator(registry, provider).ValidateOrThrowAsync();
    }

    [Fact]
    public async Task RuntimeBindingsReportMissingDuplicateAndMismatchedRegistrations()
    {
        var missing = MockWithCode<IDocumentTypeStorage>("missing", x => x.TypeCode);
        using var missingProvider = new ServiceCollection().BuildServiceProvider();
        var claimsRegistered = new Mock<IServiceProviderIsService>();
        claimsRegistered.Setup(x => x.IsService(missing.Object.GetType())).Returns(true);
        var missingRegistry = Registry(documents:
        [
            new DocumentTypeDefinition("missing", new DocumentTypeMetadata("missing", []),
                typedStorageType: missing.Object.GetType())
        ]);

        var missingErrors = await ValidateRuntimeAsync(missingRegistry, missingProvider, claimsRegistered.Object);
        missingErrors.Should().ContainSingle(x => x.Contains("is not registered in DI as IDocumentTypeStorage"));

        var duplicateA = MockWithCode<IDocumentTypeStorage>("duplicate", x => x.TypeCode);
        var duplicateB = MockWithCode<IDocumentTypeStorage>("duplicate", x => x.TypeCode);
        var duplicateServices = new ServiceCollection();
        duplicateServices.AddSingleton<IDocumentTypeStorage>(duplicateA.Object);
        duplicateServices.AddSingleton<IDocumentTypeStorage>(duplicateB.Object);
        duplicateServices.AddSingleton(duplicateA.Object.GetType(), duplicateA.Object);
        using var duplicateProvider = duplicateServices.BuildServiceProvider();
        var duplicateRegistry = Registry(documents:
        [
            new DocumentTypeDefinition("duplicate", new DocumentTypeMetadata("duplicate", []),
                typedStorageType: duplicateA.Object.GetType())
        ]);

        var duplicateErrors = await ValidateRuntimeAsync(duplicateRegistry, duplicateProvider);
        duplicateErrors.Should().ContainSingle(x => x.Contains("multiple matching DI registrations") && x.Contains("count=2"));

        var mismatch = MockWithCode<ICatalogTypeStorage>("actual", x => x.CatalogCode);
        var mismatchServices = new ServiceCollection();
        var mismatchType = Register(mismatchServices, mismatch.Object);
        using var mismatchProvider = mismatchServices.BuildServiceProvider();
        var mismatchRegistry = Registry(catalogs:
        [
            new CatalogTypeDefinition("expected", CatalogMetadata("expected", []), mismatchType)
        ]);

        var mismatchErrors = await ValidateRuntimeAsync(mismatchRegistry, mismatchProvider);
        mismatchErrors.Should().ContainSingle(x => x.Contains("resolved CatalogCode 'actual'") && x.Contains("definition code 'expected'"));

        var nullIsService = new DefinitionsValidationService(
            mismatchRegistry,
            isService: null,
            mismatchProvider.GetRequiredService<IServiceScopeFactory>());
        (await ((Func<Task>)(() => nullIsService.ValidateOrThrowAsync()))
                .Should().ThrowAsync<DefinitionsValidationException>())
            .Which.Errors.Should().Contain(x => x.Contains("IServiceProviderIsService is not available"));
    }

    [Fact]
    public async Task RuntimeBindingValidationSkipsAbsentInvalidAndUnregisteredConcreteCandidates()
    {
        var unregistered = MockWithCode<IDocumentNumberingPolicy>("doc", x => x.TypeCode);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var document = new DocumentTypeDefinition(
            "doc",
            new DocumentTypeMetadata("doc", []),
            typedStorageType: null,
            postingHandlerType: typeof(IDocumentPostingHandler),
            operationalRegisterPostingHandlerType: typeof(AbstractOperationalPosting),
            referenceRegisterPostingHandlerType: typeof(OpenReferencePosting<>),
            numberingPolicyType: unregistered.Object.GetType(),
            approvalPolicyType: typeof(string));

        var errors = await ValidateRuntimeAsync(Registry(documents: [document]), provider);

        errors.Should().Contain(x => x.Contains("PostingHandlerType must be a concrete type"));
        errors.Should().Contain(x => x.Contains("OperationalRegisterPostingHandlerType must be a concrete type"));
        errors.Should().Contain(x => x.Contains("ReferenceRegisterPostingHandlerType must be a closed constructed type"));
        errors.Should().Contain(x => x.Contains("NumberingPolicyType") && x.Contains("is not registered in DI"));
        errors.Should().Contain(x => x.Contains("ApprovalPolicyType must implement IDocumentApprovalPolicy"));
        errors.Should().NotContain(x => x.Contains("is not registered in DI as"));
    }

    private static async Task<IReadOnlyList<string>> ValidateAsync(DefinitionsRegistry registry)
    {
        var exception = (await ((Func<Task>)(() => new DefinitionsValidationService(registry).ValidateOrThrowAsync()))
                .Should().ThrowAsync<DefinitionsValidationException>()).Which;
        return exception.Errors;
    }

    private static async Task<IReadOnlyList<string>> ValidateRuntimeAsync(
        DefinitionsRegistry registry,
        ServiceProvider provider,
        IServiceProviderIsService? isService = null)
    {
        var exception = (await ((Func<Task>)(() => CreateRuntimeValidator(registry, provider, isService).ValidateOrThrowAsync()))
                .Should().ThrowAsync<DefinitionsValidationException>()).Which;
        return exception.Errors;
    }

    private static DefinitionsValidationService CreateRuntimeValidator(
        DefinitionsRegistry registry,
        ServiceProvider provider,
        IServiceProviderIsService? isService = null)
        => new(
            registry,
            isService ?? provider.GetRequiredService<IServiceProviderIsService>(),
            provider.GetRequiredService<IServiceScopeFactory>());

    private static Mock<T> MockWithCode<T>(string code, System.Linq.Expressions.Expression<Func<T, string>> property)
        where T : class
    {
        var mock = new Mock<T>();
        mock.SetupGet(property).Returns(code);
        return mock;
    }

    private static Type Register<T>(IServiceCollection services, T instance)
        where T : class
    {
        services.AddSingleton(typeof(T), instance);
        services.AddSingleton(instance.GetType(), instance);
        return instance.GetType();
    }

    private static DefinitionsRegistry Registry(
        IEnumerable<DocumentTypeDefinition>? documents = null,
        IEnumerable<CatalogTypeDefinition>? catalogs = null,
        IEnumerable<DocumentRelationshipTypeDefinition>? relationships = null,
        IEnumerable<DocumentDerivationDefinition>? derivations = null)
        => new(
            Dictionary(documents ?? [], x => x.TypeCode),
            Dictionary(catalogs ?? [], x => x.TypeCode),
            Dictionary(relationships ?? [], x => x.Code),
            Dictionary(derivations ?? [], x => x.Code));

    private static IReadOnlyDictionary<string, T> Dictionary<T>(IEnumerable<T> items, Func<T, string> code)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in items)
        {
            var key = code(item);
            if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
                key = $"__invalid_{index}";
            result[key] = item;
            index++;
        }
        return result;
    }

    private static DocumentTypeDefinition Doc(
        string code,
        IReadOnlyList<DocumentTableMetadata>? tables = null,
        DocumentPresentationMetadata? presentation = null,
        string? metadataCode = null)
        => new(code, new DocumentTypeMetadata(metadataCode ?? code, tables ?? [], presentation));

    private static DocumentTableMetadata Head(string name, params DocumentColumnMetadata[] columns)
        => new(name, TableKind.Head, columns);

    private static DocumentTableMetadata Head(string name, IReadOnlyList<DocumentColumnMetadata>? columns = null, string? partCode = null)
        => new(name, TableKind.Head, columns ?? [], PartCode: partCode);

    private static DocumentTableMetadata Part(string name, string? partCode)
        => new(name, TableKind.Part, [], PartCode: partCode);

    private static DocumentColumnMetadata Col(string name, ColumnType type)
        => new(name, type);

    private static DocumentColumnMetadata Mirror(string name, ColumnType type, string relationship,
        LookupSourceMetadata? lookup)
        => new(name, type, Lookup: lookup, MirroredRelationship: new MirroredDocumentRelationshipMetadata(relationship));

    private static CatalogTypeDefinition Catalog(string code, IReadOnlyList<CatalogTableMetadata>? tables = null,
        string? metadataCode = null)
        => new(code, CatalogMetadata(metadataCode ?? code, tables ?? []));

    private static CatalogTypeMetadata CatalogMetadata(string code, IReadOnlyList<CatalogTableMetadata> tables)
        => new(code, code, tables, new CatalogPresentationMetadata(code, "name"), new CatalogMetadataVersion(1, "tests"));

    private static CatalogTableMetadata CatalogHead(string name, string? partCode = null)
        => new(name, TableKind.Head, [], [], partCode);

    private static CatalogTableMetadata CatalogPart(string name, string? partCode)
        => new(name, TableKind.Part, [], [], partCode);

    private static DocumentRelationshipTypeDefinition Rel(string code, string name = "Relationship",
        bool bidirectional = false, IReadOnlyCollection<string>? allowedFrom = null,
        IReadOnlyCollection<string>? allowedTo = null)
        => new(code, name, bidirectional, DocumentRelationshipCardinality.ManyToMany, allowedFrom, allowedTo);

    private static DocumentDerivationDefinition Derivation(string code, string name, string from, string to,
        IReadOnlyList<string> relationships, Type? handler = null)
        => new(code, name, from, to, relationships, handler);

    private abstract class AbstractMarker;
    private abstract class AbstractOperationalPosting : IDocumentOperationalRegisterPostingHandler
    {
        public abstract string TypeCode { get; }
        public abstract Task BuildMovementsAsync(
            NGB.Core.Documents.DocumentRecord document,
            NGB.OperationalRegisters.Contracts.IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct);
    }

    private sealed class OpenReferencePosting<T> : IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => "doc";

        public Task BuildRecordsAsync(
            NGB.Core.Documents.DocumentRecord document,
            NGB.ReferenceRegisters.Contracts.ReferenceRegisterWriteOperation operation,
            NGB.ReferenceRegisters.Contracts.IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
            => Task.CompletedTask;
    }
}
