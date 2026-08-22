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

    private sealed class Fixture
    {
        public Fixture(Type? handlerType, IReadOnlyList<IDocumentDerivationHandler> handlers)
        {
            var builder = new DefinitionsBuilder();
            builder.AddDocumentDerivation("derive", definition =>
            {
                definition
                    .Name("Derive document")
                    .From("source")
                    .To("target")
                    .Relationship("based_on");
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
