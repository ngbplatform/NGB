using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Actions;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;
using DocumentActionConfirmationMode = NGB.Core.Documents.Actions.DocumentActionConfirmationMode;
using DocumentActionExecutionKind = NGB.Core.Documents.Actions.DocumentActionExecutionKind;
using DocumentActionKind = NGB.Core.Documents.Actions.DocumentActionKind;

namespace NGB.Runtime.Tests.Documents.Actions;

public sealed class DocumentActionEvaluatorCoverageTests
{
    private const string SourceType = "test.source";
    private const string TargetType = "test.target";

    [Fact]
    public async Task LoadFacts_uses_the_matching_enricher_and_rejects_duplicates()
    {
        var document = Document();
        var dto = Dto(document.Id);
        var snapshot = Snapshot(bootstrapAdmin: true);
        var matching = new TestEnricher(SourceType, new Dictionary<string, object?> { ["balance"] = 42m });
        var unrelated = new TestEnricher(TargetType, new Dictionary<string, object?>());
        var evaluator = CreateEvaluator(enrichers: [matching, unrelated]);

        var facts = await evaluator.LoadFactsAsync(document, dto, snapshot, CancellationToken.None);

        facts["balance"].Should().Be(42m);
        matching.CallCount.Should().Be(1);
        unrelated.CallCount.Should().Be(0);

        var duplicateEvaluator = CreateEvaluator(
            enrichers: [matching, new TestEnricher(SourceType, new Dictionary<string, object?>())]);
        var duplicate = () => duplicateEvaluator.LoadFactsAsync(document, dto, snapshot, CancellationToken.None);
        await duplicate.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Only one*enricher*");
    }

    [Fact]
    public async Task Custom_authorization_and_availability_are_resolved_and_reasons_are_sorted()
    {
        var services = new ServiceCollection()
            .AddSingleton<AllowAuthorizationEvaluator>()
            .AddSingleton<DenyAuthorizationEvaluator>()
            .AddSingleton<SortedAvailabilityEvaluator>()
            .BuildServiceProvider();
        var document = Document(status: DocumentStatus.Posted);
        var dto = Dto(document.Id);
        var snapshot = Snapshot();
        var evaluator = CreateEvaluator(services: services);

        var allowedDefinition = Definition(
            "test.custom",
            authorizationEvaluatorType: typeof(AllowAuthorizationEvaluator),
            availabilityEvaluatorType: typeof(SortedAvailabilityEvaluator));
        var evaluated = await evaluator.EvaluateOneAsync(
            allowedDefinition,
            document,
            dto,
            snapshot,
            new Dictionary<string, object?>(),
            CancellationToken.None);

        evaluated.Definition.Should().BeSameAs(allowedDefinition);
        evaluated.Dto.IsAllowed.Should().BeFalse();
        evaluated.Dto.DisabledReasons.Select(static reason => reason.Code).Should().Equal("a.reason", "z.reason");

        var deniedDefinition = Definition(
            "test.denied",
            authorizationEvaluatorType: typeof(DenyAuthorizationEvaluator));
        var denied = () => evaluator.EvaluateOneAsync(
            deniedDefinition,
            document,
            dto,
            snapshot,
            new Dictionary<string, object?>(),
            CancellationToken.None);
        await denied.Should().ThrowAsync<DocumentActionForbiddenException>();

        var viewAuthorized = await evaluator.EvaluateOneAsync(
            Definition("test.view-authorized"),
            document,
            dto,
            Snapshot(permissions: [Permission(SourceType, NgbPermissionActions.View)]),
            new Dictionary<string, object?>(),
            CancellationToken.None);
        viewAuthorized.Dto.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Derivation_authorization_requires_source_view_target_create_and_target_view()
    {
        var (definitions, registry) = DerivationDefinitions();
        var evaluator = CreateEvaluator(definitions, registry);
        var document = Document();
        var dto = Dto(document.Id);
        var definition = registry.Get(SourceType, new DocumentActionCode("test.derive"));

        var permissionSets = new[]
        {
            Array.Empty<NgbPermissionKey>(),
            new[] { Permission(SourceType, NgbPermissionActions.View) },
            new[]
            {
                Permission(SourceType, NgbPermissionActions.View),
                Permission(TargetType, NgbPermissionActions.Create)
            }
        };

        foreach (var permissions in permissionSets)
        {
            var denied = () => evaluator.EvaluateOneAsync(
                definition,
                document,
                dto,
                Snapshot(permissions: permissions),
                new Dictionary<string, object?>(),
                CancellationToken.None);
            await denied.Should().ThrowAsync<DocumentActionForbiddenException>();
        }

        var allowed = await evaluator.EvaluateOneAsync(
            definition,
            document,
            dto,
            Snapshot(
                permissions:
                [
                    Permission(SourceType, NgbPermissionActions.View),
                    Permission(TargetType, NgbPermissionActions.Create),
                    Permission(TargetType, NgbPermissionActions.View)
                ]),
            new Dictionary<string, object?>(),
            CancellationToken.None);
        allowed.Dto.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registry_rejects_derivations_with_unknown_source_or_target(bool missingSource)
    {
        var builder = new DefinitionsBuilder();
        if (!missingSource)
            builder.AddDocument(SourceType, definition => definition.Metadata(MinimalMetadata(SourceType)));
        if (missingSource)
            builder.AddDocument(TargetType, definition => definition.Metadata(MinimalMetadata(TargetType)));
        builder.AddDocumentDerivation(
            "test.invalid-derive",
            definition => definition
                .Name("Invalid derive")
                .From(SourceType)
                .To(TargetType)
                .Relationship("based_on"));

        var action = () => new DocumentActionRegistry(builder.Build(), []);

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*references an unknown document type*");
    }

    [Fact]
    public void Registry_key_comparer_compares_both_document_and_action_components()
    {
        var comparerType = typeof(DocumentActionRegistry).GetNestedType(
            "DocumentActionKeyComparer",
            BindingFlags.NonPublic);
        var instance = comparerType!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null);
        var comparer = (IEqualityComparer<(string DocumentType, string ActionCode)>)instance!;

        comparer.Equals(("test.source", "post"), ("TEST.SOURCE", "POST")).Should().BeTrue();
        comparer.Equals(("test.source", "post"), ("test.source", "unpost")).Should().BeFalse();
        comparer.Equals(("test.source", "post"), ("test.target", "post")).Should().BeFalse();
    }

    [Fact]
    public void Evaluators_with_constructor_dependencies_are_rejected_at_configuration_time()
    {
        var action = () => DocumentActionComponentResolver.EnsurePureEvaluator(
            typeof(IoBoundAvailabilityEvaluator));

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must be pure*IDocumentActionContextEnricher*");
    }

    [Fact]
    public void Target_tokens_support_fields_documents_created_documents_and_null_values()
    {
        var document = Document();
        var createdId = Guid.NewGuid();
        var fields = new Dictionary<string, JsonElement>
        {
            ["text"] = JsonSerializer.SerializeToElement("hello"),
            ["reference"] = JsonSerializer.SerializeToElement(new { id = createdId.ToString(), display = "Created" }),
            ["objectWithoutStringId"] = JsonSerializer.SerializeToElement(new { id = 7 }),
            ["number"] = JsonSerializer.SerializeToElement(12)
        };
        var dto = new DocumentDto(
            document.Id,
            "Source",
            new RecordPayload(fields, null),
            NGB.Contracts.Metadata.DocumentStatus.Draft,
            false);
        var definition = new DocumentActionDefinition(
            SourceType,
            new DocumentActionMetadata(
                new DocumentActionCode("test.navigate"),
                new DocumentActionPresentation("Navigate"),
                DocumentActionKind.Secondary,
                DocumentActionExecutionKind.Navigation,
                1,
                new DocumentActionConfirmationMetadata(
                    DocumentActionConfirmationMode.Confirm,
                    "Confirm",
                    "Continue?",
                    "Continue"),
                new DocumentActionTargetMetadata(
                    "test.target",
                    new Dictionary<string, string?>
                    {
                        ["null"] = null,
                        ["text"] = "{field:text}",
                        ["reference"] = "{field:reference}",
                        ["objectFallback"] = "{field:objectWithoutStringId}",
                        ["number"] = "{field:number}",
                        ["missing"] = "{field:missing}",
                        ["document"] = "{documentType}/{documentId}",
                        ["created"] = "{createdDocumentId}"
                    })));
        var evaluator = CreateEvaluator();

        var target = evaluator.ToDto(
            definition,
            DocumentActionAvailabilityResult.Allowed,
            document,
            dto,
            createdId).Target!;

        target.Parameters["null"].Should().BeNull();
        target.Parameters["text"].Should().Be("hello");
        target.Parameters["reference"].Should().Be(createdId.ToString());
        target.Parameters["objectFallback"].Should().Be("{\"id\":7}");
        target.Parameters["number"].Should().Be("12");
        target.Parameters["missing"].Should().BeNull();
        target.Parameters["document"].Should().Be($"{SourceType}/{document.Id}");
        target.Parameters["created"].Should().Be(createdId.ToString());

        evaluator.ToDto(
                definition,
                DocumentActionAvailabilityResult.Allowed,
                document,
                dto,
                createdDocumentId: null)
            .Target!.Parameters["created"].Should().Be("{createdDocumentId}");

        var dtoWithoutFields = new DocumentDto(
            document.Id,
            "Source",
            new RecordPayload(null, null),
            NGB.Contracts.Metadata.DocumentStatus.Draft,
            false);
        evaluator.ToDto(
                definition,
                DocumentActionAvailabilityResult.Allowed,
                document,
                dtoWithoutFields,
                createdId)
            .Target!.Parameters["text"].Should().BeNull();
    }

    private static DocumentActionEvaluator CreateEvaluator(
        DefinitionsRegistry? definitions = null,
        DocumentActionRegistry? registry = null,
        IServiceProvider? services = null,
        IReadOnlyList<IDocumentActionContextEnricher>? enrichers = null)
    {
        definitions ??= BaseDefinitions();
        registry ??= new DocumentActionRegistry(definitions, []);
        services ??= new ServiceCollection().BuildServiceProvider();
        return new DocumentActionEvaluator(
            registry,
            definitions,
            new DocumentActionComponentResolver(services),
            enrichers ?? []);
    }

    private static DefinitionsRegistry BaseDefinitions()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument(SourceType, definition => definition.Metadata(MinimalMetadata(SourceType)));
        builder.AddDocument(TargetType, definition => definition.Metadata(MinimalMetadata(TargetType)));
        return builder.Build();
    }

    private static (DefinitionsRegistry Definitions, DocumentActionRegistry Registry) DerivationDefinitions()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument(SourceType, definition => definition.Metadata(MinimalMetadata(SourceType)));
        builder.AddDocument(TargetType, definition => definition.Metadata(MinimalMetadata(TargetType)));
        builder.AddDocumentDerivation(
            "test.derive",
            definition => definition
                .Name("Derive")
                .From(SourceType)
                .To(TargetType)
                .Relationship("based_on"));
        var definitions = builder.Build();
        return (definitions, new DocumentActionRegistry(definitions, []));
    }

    private static DocumentActionDefinition Definition(
        string code,
        Type? authorizationEvaluatorType = null,
        Type? availabilityEvaluatorType = null)
        => new(
            SourceType,
            new DocumentActionMetadata(
                new DocumentActionCode(code),
                new DocumentActionPresentation("Test"),
                DocumentActionKind.Secondary,
                DocumentActionExecutionKind.Command,
                1),
            HandlerType: typeof(NoOpHandler),
            AvailabilityEvaluatorType: availabilityEvaluatorType,
            AuthorizationEvaluatorType: authorizationEvaluatorType);

    private static DocumentRecord Document(DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = SourceType,
            DateUtc = DateTime.UtcNow,
            Status = status,
            Version = 1
        };

    private static DocumentDto Dto(Guid id)
        => new(
            id,
            "Source",
            new RecordPayload(),
            NGB.Contracts.Metadata.DocumentStatus.Draft,
            false);

    private static PermissionSnapshot Snapshot(
        bool bootstrapAdmin = false,
        IEnumerable<NgbPermissionKey>? permissions = null)
        => new(
            Guid.NewGuid(),
            "subject",
            true,
            true,
            bootstrapAdmin,
            1,
            new HashSet<NgbPermissionKey>(permissions ?? []));

    private static NgbPermissionKey Permission(string type, string action)
        => new(NgbResourceKinds.Document, type, action);

    private static DocumentTypeMetadata MinimalMetadata(string typeCode)
        => new(typeCode, []);

    private sealed class TestEnricher(
        string documentTypeCode,
        IReadOnlyDictionary<string, object?> facts) : IDocumentActionContextEnricher
    {
        public string DocumentTypeCode { get; } = documentTypeCode;
        public int CallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>> LoadFactsAsync(
            DocumentActionContextRequest request,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(facts);
        }
    }

    private sealed class AllowAuthorizationEvaluator : IDocumentActionAuthorizationEvaluator
    {
        public ValueTask<DocumentActionAuthorizationResult> EvaluateAsync(
            DocumentActionEvaluationContext context,
            CancellationToken ct)
            => ValueTask.FromResult(DocumentActionAuthorizationResult.Authorized);
    }

    private sealed class DenyAuthorizationEvaluator : IDocumentActionAuthorizationEvaluator
    {
        public ValueTask<DocumentActionAuthorizationResult> EvaluateAsync(
            DocumentActionEvaluationContext context,
            CancellationToken ct)
            => ValueTask.FromResult(DocumentActionAuthorizationResult.Denied);
    }

    private sealed class SortedAvailabilityEvaluator : IDocumentActionAvailabilityEvaluator
    {
        public ValueTask<DocumentActionAvailabilityResult> EvaluateAsync(
            DocumentActionEvaluationContext context,
            CancellationToken ct)
            => ValueTask.FromResult(
                new DocumentActionAvailabilityResult(
                [
                    new DocumentActionDisabledReasonDto("z.reason", "Second"),
                    new DocumentActionDisabledReasonDto("a.reason", "First")
                ]));
    }

    private sealed class IoBoundAvailabilityEvaluator(IDocumentService documents)
        : IDocumentActionAvailabilityEvaluator
    {
        public ValueTask<DocumentActionAvailabilityResult> EvaluateAsync(
            DocumentActionEvaluationContext context,
            CancellationToken ct)
        {
            _ = documents;
            return ValueTask.FromResult(DocumentActionAvailabilityResult.Allowed);
        }
    }

    private sealed class NoOpHandler : IDocumentActionHandler
    {
        public Task<DocumentActionHandlerResult> ExecuteAsync(
            DocumentActionHandlerContext context,
            CancellationToken ct)
            => Task.FromResult(new DocumentActionHandlerResult());
    }
}
