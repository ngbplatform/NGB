using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Accounting.Posting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Events;
using NGB.Core.Security;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Actions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Actions;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Observability;
using NGB.Runtime.Security;
using NGB.Runtime.Ui;
using NGB.Tools.Exceptions;
using Xunit;
using ContractStatus = NGB.Contracts.Metadata.DocumentStatus;
using CoreStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.Runtime.Tests.Documents.Actions;

[Collection(Observability.TelemetrySerialCollection.Name)]
public sealed class DocumentActionDispatcherCoverageTests
{
    private const string SourceType = "test.source";
    private const string TargetType = "test.target";

    [Theory]
    [InlineData(null, "key", 1, typeof(NgbArgumentRequiredException))]
    [InlineData(" ", "key", 1, typeof(NgbArgumentRequiredException))]
    [InlineData(SourceType, null, 1, typeof(NgbArgumentRequiredException))]
    [InlineData(SourceType, " ", 1, typeof(NgbArgumentRequiredException))]
    [InlineData(SourceType, "key", 0, typeof(NgbArgumentInvalidException))]
    public async Task Execute_validates_request_contract(
        string? documentType,
        string? idempotencyKey,
        long expectedVersion,
        Type errorType)
    {
        var harness = new Harness();

        var action = () => harness.Dispatcher.ExecuteAsync(
            documentType!,
            harness.Source.Id,
            StandardDocumentActionCodes.Post,
            idempotencyKey!,
            new ExecuteDocumentActionRequestDto(expectedVersion),
            CancellationToken.None);

        (await action.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(errorType);
    }

    [Fact]
    public async Task Execute_rejects_null_request_and_client_side_targets()
    {
        var harness = new Harness();

        var nullRequest = () => harness.Dispatcher.ExecuteAsync(
            SourceType,
            harness.Source.Id,
            StandardDocumentActionCodes.Post,
            "key",
            null!,
            CancellationToken.None);
        await nullRequest.Should().ThrowAsync<NgbArgumentRequiredException>();

        var view = () => harness.Dispatcher.ExecuteAsync(
            SourceType,
            harness.Source.Id,
            StandardDocumentActionCodes.ViewFlow,
            "key",
            new ExecuteDocumentActionRequestDto(1),
            CancellationToken.None);
        var error = await view.Should().ThrowAsync<DocumentActionUnavailableException>();
        error.Which.Context["reasonCodes"].Should().BeEquivalentTo(
            new[] { "document_action.client_side_target" });
    }

    [Fact]
    public async Task Execute_rejects_missing_and_mismatched_documents_before_begin()
    {
        var missing = new Harness();
        missing.Documents
            .Setup(repository => repository.GetAsync(missing.Source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        var missingAction = () => missing.ExecuteAsync();
        await missingAction.Should().ThrowAsync<DocumentNotFoundException>();

        var mismatched = new Harness();
        mismatched.Source = Clone(mismatched.Source, typeCode: TargetType);
        var mismatchAction = () => mismatched.ExecuteAsync();
        await mismatchAction.Should().ThrowAsync<DocumentTypeMismatchException>();
    }

    [Fact]
    public async Task Execute_repeats_type_check_under_lock()
    {
        var harness = new Harness();
        var locked = Clone(harness.Source, typeCode: TargetType);
        harness.Documents
            .Setup(repository => repository.GetForUpdateAsync(harness.Source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locked);

        var action = () => harness.ExecuteAsync();

        await action.Should().ThrowAsync<DocumentTypeMismatchException>();
        harness.Uow.Verify(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_rejects_a_document_that_disappears_before_lock()
    {
        var harness = new Harness();
        harness.Documents
            .Setup(repository => repository.GetForUpdateAsync(harness.Source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var action = () => harness.ExecuteAsync();

        await action.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Theory]
    [InlineData(DocumentActionExecutionBeginStatus.Conflict, typeof(DocumentActionIdempotencyConflictException))]
    [InlineData(DocumentActionExecutionBeginStatus.InProgress, typeof(DocumentActionInProgressException))]
    public async Task Execute_maps_idempotency_begin_statuses(
        DocumentActionExecutionBeginStatus status,
        Type errorType)
    {
        var harness = new Harness(beginStatus: status);

        var action = () => harness.ExecuteAsync();

        (await action.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(errorType);
    }

    [Fact]
    public async Task Execute_replays_completed_result_without_mutating_document()
    {
        var harness = new Harness(beginStatus: DocumentActionExecutionBeginStatus.Completed);
        var stored = new ExecuteDocumentActionResultDto(
            harness.ExecutionId,
            StandardDocumentActionCodes.Post.Value,
            Dto(harness.Source),
            harness.Source.Version,
            [],
            WorkCenterMayChange: true);
        harness.StoredResultJson = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await harness.ExecuteAsync();

        result.ExecutionId.Should().Be(harness.ExecutionId);
        result.Document.Id.Should().Be(harness.Source.Id);
        harness.Posting.Verify(
            service => service.PostAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Executions.Verify(
            repository => repository.MarkCompletedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_rejects_an_invalid_completed_result_invariant()
    {
        var harness = new Harness(beginStatus: DocumentActionExecutionBeginStatus.Completed)
        {
            StoredResultJson = "null"
        };

        var action = () => harness.ExecuteAsync();

        await action.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task Execute_reports_stale_versions_and_rolls_back()
    {
        var harness = new Harness();

        var action = () => harness.ExecuteAsync(expectedVersion: harness.Source.Version + 1);

        await action.Should().ThrowAsync<DocumentVersionConflictException>();
        harness.Uow.Verify(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_populates_success_and_error_activity_statuses_when_tracing_is_enabled()
    {
        using var listener = ListenToFeatureActivities();

        var success = new Harness();
        (await success.ExecuteAsync()).Document.Status.Should().Be(ContractStatus.Posted);

        var concurrency = new Harness();
        var conflict = () => concurrency.ExecuteAsync(expectedVersion: 2);
        await conflict.Should().ThrowAsync<DocumentVersionConflictException>();

        var generic = new Harness();
        var clientSide = () => generic.ExecuteAsync(StandardDocumentActionCodes.ViewFlow);
        await clientSide.Should().ThrowAsync<DocumentActionUnavailableException>();
    }

    [Fact]
    public async Task Execute_rechecks_authorization_and_availability_inside_transaction()
    {
        var forbidden = new Harness();
        forbidden.Permissions
            .SetupSequence(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(forbidden.Snapshot(bootstrapAdmin: true));
        forbidden.Permissions
            .Setup(provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(forbidden.Snapshot(bootstrapAdmin: false));
        var forbiddenAction = () => forbidden.ExecuteAsync();
        await forbiddenAction.Should().ThrowAsync<DocumentActionForbiddenException>();

        var unavailable = new Harness(initialStatus: CoreStatus.Posted);
        var unavailableAction = () => unavailable.ExecuteAsync();
        await unavailableAction.Should().ThrowAsync<DocumentActionUnavailableException>();
    }

    [Fact]
    public async Task Execute_requires_reason_when_metadata_requires_it()
    {
        var harness = new Harness();

        var action = () => harness.ExecuteAsync(
            new DocumentActionCode("test.require-reason"),
            reason: " ");

        await action.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*reason is required*");
    }

    [Theory]
    [InlineData("post", CoreStatus.Draft, CoreStatus.Posted)]
    [InlineData("unpost", CoreStatus.Posted, CoreStatus.Draft)]
    [InlineData("repost", CoreStatus.Posted, CoreStatus.Posted)]
    [InlineData("mark_for_deletion", CoreStatus.Draft, CoreStatus.MarkedForDeletion)]
    [InlineData("unmark_for_deletion", CoreStatus.MarkedForDeletion, CoreStatus.Draft)]
    public async Task Execute_runs_each_standard_lifecycle_handler(
        string actionCode,
        CoreStatus initialStatus,
        CoreStatus expectedStatus)
    {
        var harness = new Harness(initialStatus: initialStatus);

        var result = await harness.ExecuteAsync(new DocumentActionCode(actionCode));

        result.ActionCode.Should().Be(actionCode);
        result.Document.Status.Should().Be(ToContractStatus(expectedStatus));
        result.DocumentVersion.Should().Be(2);
        result.WorkCenterMayChange.Should().BeTrue();
        harness.Executions.Verify(
            repository => repository.MarkCompletedAsync(
                harness.ExecutionId,
                It.Is<string>(json => json.Contains(actionCode, StringComparison.Ordinal)),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Outbox.Verify(
            repository => repository.AppendAsync(
                It.Is<PlatformOutboxEvent>(
                    item => item.EventType == "ngb.document.action.completed"
                            && item.CorrelationId == harness.ExecutionId),
                It.Is<IReadOnlyList<string>>(consumers => consumers.SequenceEqual(new[] { "work-center" })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_dispatches_custom_handler_with_context()
    {
        var harness = new Harness();
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });

        var result = await harness.ExecuteAsync(
            new DocumentActionCode("test.command"),
            reason: "Because",
            payload: payload);

        result.ActionCode.Should().Be("test.command");
        harness.Handler.CallCount.Should().Be(1);
        harness.Handler.LastContext.Should().NotBeNull();
        harness.Handler.LastContext!.ExecutionId.Should().Be(harness.ExecutionId);
        harness.Handler.LastContext.ActorUserId.Should().Be(harness.UserId);
        harness.Handler.LastContext.Reason.Should().Be("Because");
        harness.Handler.LastContext.Payload!.Value.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Execute_derivation_returns_created_document()
    {
        var harness = new Harness();

        var result = await harness.ExecuteAsync(new DocumentActionCode("test.derive"));

        result.CreatedDocument.Should().NotBeNull();
        result.CreatedDocument!.Id.Should().Be(harness.Created.Id);
        harness.Derivations.Verify(
            service => service.CreateDraftAsync(
                "test.derive",
                harness.Source.Id,
                null,
                null,
                null,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_rejects_missing_created_or_refreshed_documents_as_invariants()
    {
        var missingCreated = new Harness();
        missingCreated.Documents
            .Setup(repository => repository.GetAsync(missingCreated.Created.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        var createdAction = () => missingCreated.ExecuteAsync(new DocumentActionCode("test.derive"));
        await createdAction.Should().ThrowAsync<DocumentNotFoundException>();

        var missingRefreshed = new Harness();
        missingRefreshed.Documents
            .SetupSequence(
                repository => repository.GetForUpdateAsync(
                    missingRefreshed.Source.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingRefreshed.Source)
            .ReturnsAsync((DocumentRecord?)null);
        var refreshedAction = () => missingRefreshed.ExecuteAsync();
        await refreshedAction.Should().ThrowAsync<DocumentNotFoundException>();
    }

    internal sealed class Harness
    {
        public Harness(
            DocumentActionExecutionBeginStatus beginStatus = DocumentActionExecutionBeginStatus.Begun,
            CoreStatus initialStatus = CoreStatus.Draft)
        {
            Source = Record(SourceType, initialStatus);
            Created = Record(TargetType, CoreStatus.Draft);
            ExecutionId = Guid.NewGuid();
            UserId = Guid.NewGuid();

            Uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
            Uow.SetupGet(unit => unit.HasActiveTransaction).Returns(false);
            Uow.Setup(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Uow.Setup(unit => unit.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Uow.Setup(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Documents = new Mock<IDocumentRepository>(MockBehavior.Loose);
            Documents
                .Setup(repository => repository.GetAsync(Source.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Source);
            Documents
                .Setup(repository => repository.GetAsync(Created.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Created);
            Documents
                .Setup(repository => repository.GetForUpdateAsync(Source.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Source);
            Documents
                .Setup(repository => repository.IncrementVersionAsync(
                    Source.Id,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    () =>
                    {
                        Source = Clone(Source, version: Source.Version + 1);
                        return Source.Version;
                    });

            Executions = new Mock<IDocumentActionExecutionRepository>(MockBehavior.Loose);
            Executions
                .Setup(repository => repository.TryBeginAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    Source.Id,
                    SourceType,
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    () => new DocumentActionExecutionBeginResult(
                        beginStatus,
                        ExecutionId,
                        StoredResultJson));

            Outbox = new Mock<IOutboxEventRepository>(MockBehavior.Loose);
            Posting = new Mock<IDocumentPostingService>(MockBehavior.Loose);
            ConfigurePosting();
            Derivations = new Mock<IDocumentDerivationService>(MockBehavior.Loose);
            Derivations
                .Setup(service => service.CreateDraftAsync(
                    "test.derive",
                    Source.Id,
                    null,
                    null,
                    null,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Created.Id);

            Permissions = new Mock<IPermissionSnapshotProvider>(MockBehavior.Loose);
            Permissions
                .Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Snapshot(bootstrapAdmin: true));
            Permissions
                .Setup(provider => provider.RefreshCurrentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Snapshot(bootstrapAdmin: true));

            var metadata = new[] { Metadata(SourceType), Metadata(TargetType) };
            var definitionBuilder = new DefinitionsBuilder();
            definitionBuilder.AddDocument(
                SourceType,
                definition => definition
                    .Metadata(metadata[0])
                    .PostingHandler<TestPostingHandler>());
            definitionBuilder.AddDocument(
                TargetType,
                definition => definition.Metadata(metadata[1]));
            definitionBuilder.AddDocumentDerivation(
                "test.derive",
                definition => definition
                    .Name("Derive")
                    .From(SourceType)
                    .To(TargetType)
                    .Relationship("based_on"));
            var definitions = definitionBuilder.Build();
            var registry = new DocumentActionRegistry(definitions, [new TestActionContributor()]);

            Handler = new RecordingHandler();
            var services = new ServiceCollection()
                .AddSingleton(Handler)
                .BuildServiceProvider();
            var evaluator = new DocumentActionEvaluator(registry, definitions, services, []);
            Evaluator = evaluator;

            var documentTypes = new Mock<IDocumentTypeRegistry>(MockBehavior.Loose);
            documentTypes
                .Setup(registry => registry.TryGet(It.IsAny<string>()))
                .Returns(
                    (string type) => metadata.SingleOrDefault(
                        item => string.Equals(item.TypeCode, type, StringComparison.OrdinalIgnoreCase)));
            documentTypes.Setup(registry => registry.GetAll()).Returns(metadata);

            var reader = new Mock<IDocumentReader>(MockBehavior.Loose);
            reader
                .Setup(repository => repository.GetByIdAsync(
                    It.IsAny<DocumentHeadDescriptor>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (DocumentHeadDescriptor _, Guid id, CancellationToken _) =>
                    {
                        var record = id == Source.Id ? Source : id == Created.Id ? Created : null;
                        return record is null ? null : Row(record);
                    });

            var documentService = new DocumentService(
                Uow.Object,
                Documents.Object,
                new Mock<IDocumentDraftService>(MockBehavior.Loose).Object,
                documentTypes.Object,
                reader.Object,
                new Mock<IDocumentPartsReader>(MockBehavior.Loose).Object,
                new Mock<IDocumentPartsWriter>(MockBehavior.Loose).Object,
                new Mock<IDocumentWriter>(MockBehavior.Loose).Object,
                Posting.Object,
                Derivations.Object,
                new Mock<IDocumentPostingActionResolver>(MockBehavior.Loose).Object,
                new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Loose).Object,
                NoOpReferencePayloadEnricher.Instance,
                []);
            DocumentService = documentService;

            var audit = new Mock<IAuditLogService>(MockBehavior.Loose);
            Dispatcher = new DocumentActionDispatcher(
                Uow.Object,
                Documents.Object,
                Executions.Object,
                Outbox.Object,
                registry,
                evaluator,
                documentService,
                Posting.Object,
                Derivations.Object,
                Permissions.Object,
                audit.Object,
                services,
                TimeProvider.System);
        }

        public DocumentRecord Source { get; set; }
        public DocumentRecord Created { get; }
        public Guid ExecutionId { get; }
        public Guid UserId { get; }
        public string? StoredResultJson { get; set; }
        public Mock<IUnitOfWork> Uow { get; }
        public Mock<IDocumentRepository> Documents { get; }
        public Mock<IDocumentActionExecutionRepository> Executions { get; }
        public Mock<IOutboxEventRepository> Outbox { get; }
        public Mock<IDocumentPostingService> Posting { get; }
        public Mock<IDocumentDerivationService> Derivations { get; }
        public Mock<IPermissionSnapshotProvider> Permissions { get; }
        public RecordingHandler Handler { get; }
        public DocumentActionEvaluator Evaluator { get; }
        public DocumentService DocumentService { get; }
        public DocumentActionDispatcher Dispatcher { get; }

        public PermissionSnapshot Snapshot(bool bootstrapAdmin)
            => new(
                UserId,
                "subject",
                true,
                true,
                bootstrapAdmin,
                1,
                new HashSet<NgbPermissionKey>());

        public Task<ExecuteDocumentActionResultDto> ExecuteAsync(
            DocumentActionCode? actionCode = null,
            long? expectedVersion = null,
            string? reason = null,
            JsonElement? payload = null)
            => Dispatcher.ExecuteAsync(
                SourceType,
                Source.Id,
                actionCode ?? StandardDocumentActionCodes.Post,
                $"key:{Guid.NewGuid():N}",
                new ExecuteDocumentActionRequestDto(expectedVersion ?? Source.Version, payload, reason),
                CancellationToken.None);

        private void ConfigurePosting()
        {
            Posting
                .Setup(service => service.PostAsync(Source.Id, false, It.IsAny<CancellationToken>()))
                .Callback(() => Source = Clone(Source, status: CoreStatus.Posted))
                .Returns(Task.CompletedTask);
            Posting
                .Setup(service => service.UnpostAsync(Source.Id, false, It.IsAny<CancellationToken>()))
                .Callback(() => Source = Clone(Source, status: CoreStatus.Draft))
                .Returns(Task.CompletedTask);
            Posting
                .Setup(service => service.RepostAsync(Source.Id, false, It.IsAny<CancellationToken>()))
                .Callback(() => Source = Clone(Source, status: CoreStatus.Posted))
                .Returns(Task.CompletedTask);
            Posting
                .Setup(service => service.MarkForDeletionAsync(Source.Id, false, It.IsAny<CancellationToken>()))
                .Callback(() => Source = Clone(Source, status: CoreStatus.MarkedForDeletion))
                .Returns(Task.CompletedTask);
            Posting
                .Setup(service => service.UnmarkForDeletionAsync(Source.Id, false, It.IsAny<CancellationToken>()))
                .Callback(() => Source = Clone(Source, status: CoreStatus.Draft))
                .Returns(Task.CompletedTask);
        }
    }

    private sealed class TestActionContributor : IDocumentActionDefinitionsContributor
    {
        public void Contribute(DocumentActionDefinitionsBuilder builder)
        {
            builder.Add(
                SourceType,
                Command("test.command"),
                handlerType: typeof(RecordingHandler));
            builder.Add(
                SourceType,
                Command(
                    "test.require-reason",
                    new DocumentActionConfirmationMetadata(
                        DocumentActionConfirmationMode.RequireReason,
                        "Reason",
                        "Explain why.",
                        "Continue")),
                handlerType: typeof(RecordingHandler));
        }

        private static DocumentActionMetadata Command(
            string code,
            DocumentActionConfirmationMetadata? confirmation = null)
            => new(
                new DocumentActionCode(code),
                new DocumentActionPresentation("Test command"),
                DocumentActionKind.Secondary,
                DocumentActionExecutionKind.Command,
                700,
                confirmation);
    }

    internal sealed class RecordingHandler : IDocumentActionHandler
    {
        public int CallCount { get; private set; }
        public DocumentActionHandlerContext? LastContext { get; private set; }

        public Task<DocumentActionHandlerResult> ExecuteAsync(
            DocumentActionHandlerContext context,
            CancellationToken ct)
        {
            CallCount++;
            LastContext = context;
            return Task.FromResult(new DocumentActionHandlerResult());
        }
    }

    private sealed class TestPostingHandler : IDocumentPostingHandler
    {
        public string TypeCode => SourceType;

        public Task BuildEntriesAsync(
            DocumentRecord document,
            IAccountingPostingContext ctx,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private static DocumentRecord Record(string type, CoreStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = type,
            Number = null,
            DateUtc = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static DocumentRecord Clone(
        DocumentRecord source,
        string? typeCode = null,
        CoreStatus? status = null,
        long? version = null)
        => new()
        {
            Id = source.Id,
            TypeCode = typeCode ?? source.TypeCode,
            Number = source.Number,
            DateUtc = source.DateUtc,
            Status = status ?? source.Status,
            Version = version ?? source.Version,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc
        };

    private static DocumentHeadRow Row(DocumentRecord record)
        => new(
            record.Id,
            record.Status,
            record.Status == CoreStatus.MarkedForDeletion,
            $"{record.TypeCode} document",
            new Dictionary<string, object?> { ["display"] = $"{record.TypeCode} document" },
            record.Number);

    private static DocumentDto Dto(DocumentRecord record)
        => new(
            record.Id,
            $"{record.TypeCode} document",
            new RecordPayload(
                new Dictionary<string, JsonElement>
                {
                    ["display"] = JsonSerializer.SerializeToElement($"{record.TypeCode} document")
                },
                null),
            ToContractStatus(record.Status),
            record.Status == CoreStatus.MarkedForDeletion);

    private static ContractStatus ToContractStatus(CoreStatus status)
        => status switch
        {
            CoreStatus.Draft => ContractStatus.Draft,
            CoreStatus.Posted => ContractStatus.Posted,
            CoreStatus.MarkedForDeletion => ContractStatus.MarkedForDeletion,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static ActivityListener ListenToFeatureActivities()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NgbFeatureTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static DocumentTypeMetadata Metadata(string type)
        => new(
            type,
            [
                new DocumentTableMetadata(
                    $"doc_{type.Replace(".", "_", StringComparison.Ordinal)}",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                    ])
            ]);
}
