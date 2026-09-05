using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentDerivationServiceFullCoverageTests
{
    [Fact]
    public async Task ActionQueries_ValidateInput_ReportMissingDocument_AndResolveExistingType()
    {
        var fixture = new Fixture(handlerType: null, handlers: []);

        var blankType = () => fixture.Sut.ListActionsForSourceType(" ");
        var emptyId = () => fixture.Sut.ListActionsForDocumentAsync(Guid.Empty, fixture.Token);
        var missingId = Guid.CreateVersion7();
        var missingDocument = () => fixture.Sut.ListActionsForDocumentAsync(missingId, fixture.Token);

        blankType.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("sourceTypeCode");
        (await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>())
            .Which.ParamName.Should().Be("sourceDocumentId");
        var missingException = await missingDocument.Should().ThrowAsync<DocumentNotFoundException>();
        missingException.Which.DocumentId.Should().Be(missingId);

        var actions = await fixture.Sut.ListActionsForDocumentAsync(fixture.SourceId, fixture.Token);
        actions.Should().ContainSingle().Which.Code.Should().Be("derive");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenHandlerTypeDoesNotImplementContract_RollsBackWithConfigurationError()
    {
        var fixture = new Fixture(typeof(string), handlers: []);

        var act = () => fixture.CreateDraftAsync();

        var exception = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        exception.Which.Message.Should().Contain("must implement IDocumentDerivationHandler");
        exception.Which.Context.Should().Contain("handlerType", typeof(string).FullName);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenHandlerIsNotRegistered_RollsBackWithBindingDiagnostics()
    {
        var fixture = new Fixture(typeof(CountingHandler), handlers: []);

        var act = () => fixture.CreateDraftAsync();

        var exception = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        exception.Which.Message.Should().Contain("not registered");
        exception.Which.Context.Should().Contain("derivationCode", "derive");
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenMultipleHandlersMatch_RollsBackAndListsEveryRegistration()
    {
        var first = new CountingHandler();
        var second = new CountingHandler();
        var fixture = new Fixture(typeof(CountingHandler), [first, second]);

        var act = () => fixture.CreateDraftAsync();

        var exception = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        exception.Which.Message.Should().Contain("Multiple document derivation handlers");
        exception.Which.Context["matches"].Should().BeEquivalentTo(new[]
        {
            typeof(CountingHandler).ToString(),
            typeof(CountingHandler).ToString()
        });
        first.ApplyCount.Should().Be(0);
        second.ApplyCount.Should().Be(0);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_CachesResolvedHandler_AcrossSuccessfulDerivations()
    {
        var handler = new CountingHandler();
        var fixture = new Fixture(typeof(CountingHandler), [handler]);

        var first = await fixture.CreateDraftAsync();
        var second = await fixture.CreateDraftAsync();

        first.Should().Be(fixture.TargetId);
        second.Should().Be(fixture.TargetId);
        handler.ApplyCount.Should().Be(2);
        fixture.Documents.Verify(x => x.GetAsync(fixture.TargetId, fixture.Token), Times.Exactly(2));
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateDraftAsync_UsesExplicitBoundaryDate_InsteadOfSourceDate()
    {
        var fixture = new Fixture(handlerType: null, handlers: []);
        var explicitDate = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        fixture.Drafts
            .Setup(x => x.CreateDraftAsync("target", "EDGE", explicitDate, false, false, fixture.Token))
            .ReturnsAsync(fixture.TargetId);

        var result = await fixture.Sut.CreateDraftAsync(
            "derive",
            fixture.SourceId,
            dateUtc: explicitDate,
            number: "EDGE",
            ct: fixture.Token);

        result.Should().Be(fixture.TargetId);
        fixture.Drafts.Verify(
            x => x.CreateDraftAsync("target", "EDGE", explicitDate, false, false, fixture.Token),
            Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenCreatedDraftCannotBeReloaded_RollsBackWithInvariantDiagnostics()
    {
        var fixture = new Fixture(handlerType: null, handlers: []);
        fixture.Documents.Setup(x => x.GetForUpdateAsync(fixture.TargetId, fixture.Token))
            .ReturnsAsync((DocumentRecord?)null);

        Func<Task> act = () => fixture.CreateDraftAsync();

        var exception = await act.Should().ThrowAsync<DocumentDerivationInvariantViolationException>();
        exception.Which.Context.Should().Contain("reason", "draft_missing_after_create")
            .And.Contain("derivedDraftId", fixture.TargetId);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenHandlerRemovesDraft_RollsBackWithInvariantDiagnostics()
    {
        var fixture = new Fixture(typeof(CountingHandler), [new CountingHandler()]);
        fixture.Documents.Setup(x => x.GetAsync(fixture.TargetId, fixture.Token))
            .ReturnsAsync((DocumentRecord?)null);

        Func<Task> act = () => fixture.CreateDraftAsync();

        var exception = await act.Should().ThrowAsync<DocumentDerivationInvariantViolationException>();
        exception.Which.Context.Should().Contain("reason", "draft_missing_after_handler")
            .And.Contain("derivedDraftId", fixture.TargetId);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_UsesBatchLocksAndRelationships_ForNormalizedSourceSet()
    {
        var fixture = new Fixture(
            handlerType: null,
            handlers: [],
            useBatchCapabilities: true,
            includeReferenceRelationship: true);
        var additionalSourceId = Guid.CreateVersion7();

        var result = await fixture.Sut.CreateDraftAsync(
            "derive",
            fixture.SourceId,
            [Guid.Empty, additionalSourceId, fixture.SourceId, additionalSourceId],
            ct: fixture.Token);

        result.Should().Be(fixture.TargetId);
        fixture.Locks.As<IAdvisoryLockBatchManager>().Verify(x => x.LockDocumentsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.SequenceEqual(new[] { fixture.SourceId, additionalSourceId }.OrderBy(x => x))),
            fixture.Token), Times.Once);
        fixture.Locks.Verify(x => x.LockDocumentAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Relationships.As<IDocumentRelationshipBatchService>().Verify(x => x.CreateManyAsync(
            It.Is<IReadOnlyCollection<DocumentRelationshipCreateRequest>>(requests =>
                requests.Count == 3
                && requests.Count(r => r.RelationshipCode == "based_on") == 2
                && requests.Count(r => r.RelationshipCode == "references"
                    && r.ToDocumentId == fixture.SourceId) == 1),
            false,
            fixture.Token), Times.Once);
        fixture.Relationships.Verify(x => x.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDraftAsync_RejectsBlankUnknownAndMismatchedSourceType()
    {
        var fixture = new Fixture(handlerType: null, handlers: []);

        await ((Func<Task>)(() => fixture.Sut.CreateDraftAsync(" ", fixture.SourceId, ct: fixture.Token)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateDraftAsync("missing", fixture.SourceId, ct: fixture.Token)))
            .Should().ThrowAsync<DocumentDerivationNotFoundException>();

        fixture.Documents.Setup(x => x.GetForUpdateAsync(fixture.SourceId, fixture.Token))
            .ReturnsAsync(new DocumentRecord
            {
                Id = fixture.SourceId,
                TypeCode = "different",
                DateUtc = fixture.Source.DateUtc,
                Status = DocumentStatus.Posted
            });
        var mismatch = await ((Func<Task>)(() => fixture.CreateDraftAsync()))
            .Should().ThrowAsync<DocumentDerivationSourceTypeMismatchException>();
        mismatch.Which.Context.Should().Contain("actualFromTypeCode", "different");
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
    }

    private sealed class Fixture
    {
        public Fixture(
            Type? handlerType,
            IReadOnlyList<IDocumentDerivationHandler> handlers,
            bool useBatchCapabilities = false,
            bool includeReferenceRelationship = false)
        {
            var builder = new DefinitionsBuilder();
            builder.AddDocumentDerivation("derive", definition =>
            {
                definition
                    .Name("Derive document")
                    .From("source")
                    .To("target")
                    .Relationship("based_on");
                if (includeReferenceRelationship)
                    definition.Relationship("references");
                if (handlerType is not null)
                    definition.Handler(handlerType);
            });

            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(Token)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(Token)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(Token)).Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), Token)).Returns(Task.CompletedTask);
            Documents.Setup(x => x.GetAsync(SourceId, Token)).ReturnsAsync(Source);
            Documents.Setup(x => x.GetForUpdateAsync(SourceId, Token)).ReturnsAsync(Source);
            Documents.Setup(x => x.GetForUpdateAsync(TargetId, Token)).ReturnsAsync(Target);
            Documents.Setup(x => x.GetAsync(TargetId, Token)).ReturnsAsync(Target);
            Drafts
                .Setup(x => x.CreateDraftAsync("target", null, Source.DateUtc, false, false, Token))
                .ReturnsAsync(TargetId);
            Relationships
                .Setup(x => x.CreateAsync(TargetId, SourceId, "based_on", false, Token))
                .ReturnsAsync(true);
            if (useBatchCapabilities)
            {
                Locks.As<IAdvisoryLockBatchManager>()
                    .Setup(x => x.LockDocumentsAsync(
                        It.IsAny<IReadOnlyCollection<Guid>>(), Token))
                    .Returns(Task.CompletedTask);
                Relationships.As<IDocumentRelationshipBatchService>()
                    .Setup(x => x.CreateManyAsync(
                        It.IsAny<IReadOnlyCollection<DocumentRelationshipCreateRequest>>(), false, Token))
                    .ReturnsAsync(0);
            }

            Sut = new DocumentDerivationService(
                builder.Build(),
                Uow.Object,
                Locks.Object,
                Documents.Object,
                Drafts.Object,
                Relationships.Object,
                handlers);
        }

        public Guid SourceId { get; } = Guid.CreateVersion7();
        public Guid TargetId { get; } = Guid.CreateVersion7();
        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public DocumentRecord Source => new()
        {
            Id = SourceId,
            TypeCode = "source",
            DateUtc = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Posted
        };
        public DocumentRecord Target => new()
        {
            Id = TargetId,
            TypeCode = "target",
            DateUtc = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft
        };
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentRelationshipService> Relationships { get; } = new();
        public DocumentDerivationService Sut { get; }

        public Task<Guid> CreateDraftAsync()
            => Sut.CreateDraftAsync(" derive ", SourceId, ct: Token);
    }

    private sealed class CountingHandler : IDocumentDerivationHandler
    {
        public int ApplyCount { get; private set; }

        public Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }
    }
}
