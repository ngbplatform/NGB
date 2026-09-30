using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Posting;

public sealed class DefinitionsDocumentPostingActionResolverTests
{
    private static DocumentTypeMetadata MinimalMetadata(string typeCode) =>
        new(typeCode, new List<DocumentTableMetadata>());

    [Fact]
    public async Task Resolve_WhenDefinitionHasPostingHandler_ReturnsDelegateThatInvokesHandler()
    {
        // Arrange
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .PostingHandler<FakePostingHandler>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<FakePostingHandler>();
        services.AddSingleton<IDocumentPostingHandler>(sp => sp.GetRequiredService<FakePostingHandler>());
        services.AddSingleton<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentPostingActionResolver>();
        var handler = sp.GetRequiredService<FakePostingHandler>();

        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "DOC",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var postingCtx = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();

        // Act
        var action = resolver.Resolve(doc);
        await action(postingCtx.Object, cancellation.Token);
        var cachedAction = resolver.Resolve(new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "doc",
            DateUtc = doc.DateUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = doc.CreatedAtUtc,
            UpdatedAtUtc = doc.UpdatedAtUtc
        });
        await cachedAction(postingCtx.Object, cancellation.Token);

        // Assert
        handler.CallCount.Should().Be(2);
        handler.LastDocument.Should().NotBeNull();
        handler.LastContext.Should().BeSameAs(postingCtx.Object);
        handler.LastToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Constructor_WhenRequiredDependencyIsNull_ThrowsArgumentRequired()
    {
        var definitions = new DefinitionsBuilder().Build();

        Action nullDefinitions = () => new DefinitionsDocumentPostingActionResolver(
            null!,
            Array.Empty<IDocumentPostingHandler>());
        Action nullHandlers = () => new DefinitionsDocumentPostingActionResolver(definitions, null!);

        nullDefinitions.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("definitions");
        nullHandlers.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("handlers");
    }

    [Fact]
    public void TryResolve_WhenDocumentIsNull_ThrowsArgumentRequired()
    {
        var resolver = new DefinitionsDocumentPostingActionResolver(
            new DefinitionsBuilder().Build(),
            Array.Empty<IDocumentPostingHandler>());

        Action action = () => resolver.TryResolve(null!);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("document");
    }

    [Fact]
    public void Resolve_WhenDefinitionMissing_Throws_DocumentTypeNotFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DefinitionsBuilder().Build());
        services.AddSingleton<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();
        var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "MISSING",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        Action act = () => resolver.Resolve(doc);
        act.Should().Throw<DocumentTypeNotFoundException>()
            .Which.ErrorCode.Should().Be("doc.type.not_found");
    }

    [Fact]
    public void Resolve_WhenPostingHandlerMissing_Throws_DocumentPostingHandlerNotConfigured()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d.Metadata(MinimalMetadata("DOC")));
        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();
        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentPostingActionResolver>();

        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "DOC",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        Action act = () => resolver.Resolve(doc);
        act.Should().Throw<DocumentPostingHandlerNotConfiguredException>()
            .Which.ErrorCode.Should().Be("doc.posting.handler.not_configured");
        resolver.TryResolve(doc).Should().BeNull();
    }

    [Fact]
    public void Resolve_WhenConfiguredHandlerIsNotRegistered_Throws_Misconfigured()
    {
        var definitions = DefinitionsWithHandler<FakePostingHandler>();
        var resolver = new DefinitionsDocumentPostingActionResolver(
            definitions,
            Array.Empty<IDocumentPostingHandler>());

        Action action = () => resolver.Resolve(Document("DOC"));

        action.Should().Throw<DocumentPostingHandlerMisconfiguredException>()
            .WithMessage("*not registered in DI container*");
    }

    [Fact]
    public void Resolve_WhenConfiguredHandlerHasDuplicateRegistrations_Throws_Misconfigured()
    {
        var definitions = DefinitionsWithHandler<FakePostingHandler>();
        var resolver = new DefinitionsDocumentPostingActionResolver(
            definitions,
            new IDocumentPostingHandler[] { new FakePostingHandler(), new FakePostingHandler() });

        Action action = () => resolver.Resolve(Document("DOC"));

        action.Should().Throw<DocumentPostingHandlerMisconfiguredException>()
            .WithMessage("*Multiple posting handler registrations*");
    }

    [Fact]
    public void Resolve_WhenHandlerTypeDoesNotImplementContract_Throws_Misconfigured()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .PostingHandler(typeof(NotAHandler)));
        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<NotAHandler>();
        services.AddSingleton<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();
        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentPostingActionResolver>();

        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "DOC",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        Action act = () => resolver.Resolve(doc);
        act.Should().Throw<DocumentPostingHandlerMisconfiguredException>()
            .Which.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
    }

    [Fact]
    public void Resolve_WhenHandlerTypeCodeMismatch_Throws_Misconfigured()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .PostingHandler<MismatchedTypeCodeHandler>());
        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<MismatchedTypeCodeHandler>();
        services.AddSingleton<IDocumentPostingHandler>(sp => sp.GetRequiredService<MismatchedTypeCodeHandler>());
        services.AddSingleton<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();
        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentPostingActionResolver>();

        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "DOC",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        Action act = () => resolver.Resolve(doc);
        act.Should().Throw<DocumentPostingHandlerMisconfiguredException>()
            .Which.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
    }

    private sealed class FakePostingHandler : IDocumentPostingHandler
    {
        public int CallCount { get; private set; }
        public DocumentRecord? LastDocument { get; private set; }
        public IAccountingPostingContext? LastContext { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public string TypeCode => "DOC";

        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
        {
            CallCount++;
            LastDocument = document;
            LastContext = ctx;
            LastToken = ct;
            return Task.CompletedTask;
        }
    }

    private sealed class MismatchedTypeCodeHandler : IDocumentPostingHandler
    {
        public string TypeCode => "OTHER";
        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class NotAHandler;

    private static DefinitionsRegistry DefinitionsWithHandler<THandler>()
        where THandler : class, IDocumentPostingHandler
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", definition => definition
            .Metadata(MinimalMetadata("DOC"))
            .PostingHandler<THandler>());
        return builder.Build();
    }

    private static DocumentRecord Document(string typeCode) => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = typeCode,
        DateUtc = DateTime.UtcNow,
        Status = DocumentStatus.Draft,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
