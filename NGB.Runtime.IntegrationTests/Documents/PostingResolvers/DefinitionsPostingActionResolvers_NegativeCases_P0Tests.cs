using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Accounting.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.PostingResolvers;

public sealed class DefinitionsPostingActionResolvers_NegativeCases_P0Tests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    private PostgresTestFixture Fixture { get; } = fixture;

    private const string Doc = "doc.test";

    [Fact]
    public void Resolve_Accounting_WhenHandlerTypeDoesNotImplementContract_Throws_Misconfigured()
    {
        using var host = CreateHost(new TestDefinitionsContributor(postingHandlerType: typeof(NotAPostingHandler)));
        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("accounting");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(NotAPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("must implement");
    }

    [Fact]
    public void Resolve_Accounting_WhenHandlerNotRegistered_Throws_Misconfigured()
    {
        using var host = CreateHost(
            new TestDefinitionsContributor(postingHandlerType: typeof(UnregisteredPostingHandler)),
            configureTestServices: services =>
            {
                // Intentionally NOT registering the posting handler in DI.
            });

        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("accounting");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(UnregisteredPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("is not registered");
    }

    [Fact]
    public void Resolve_Accounting_WhenHandlerTypeCodeMismatch_Throws_Misconfigured()
    {
        using var host = CreateHost(
            new TestDefinitionsContributor(postingHandlerType: typeof(MismatchedPostingHandler)),
            configureTestServices: services => services.AddSingleton<MismatchedPostingHandler>());

        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("accounting");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(MismatchedPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("TypeCode does not match");
    }

    [Fact]
    public void Resolve_Accounting_WhenNotConfigured_Throws_NotConfigured()
    {
        using var host = CreateHost(new TestDefinitionsContributor(postingHandlerType: null));
        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.Resolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerNotConfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.not_configured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("typeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("documentId");
    }

    [Fact]
    public void Resolve_OperationalRegister_WhenNotConfigured_ReturnsNull()
    {
        using var host = CreateHost(new TestDefinitionsContributor(opregPostingHandlerType: null));
        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentOperationalRegisterPostingActionResolver>();
        var doc = NewDraft(Doc);

        var action = resolver.TryResolve(doc);
        action.Should().BeNull();
    }

    [Fact]
    public void Resolve_OperationalRegister_WhenHandlerTypeDoesNotImplementContract_Throws_Misconfigured()
    {
        using var host = CreateHost(new TestDefinitionsContributor(opregPostingHandlerType: typeof(NotAnOpRegPostingHandler)));
        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentOperationalRegisterPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("operational_register");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(NotAnOpRegPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("must implement");
    }

    [Fact]
    public void Resolve_OperationalRegister_WhenHandlerNotRegistered_Throws_Misconfigured()
    {
        using var host = CreateHost(
            new TestDefinitionsContributor(opregPostingHandlerType: typeof(UnregisteredOpRegPostingHandler)),
            configureTestServices: services =>
            {
                // Intentionally NOT registering the posting handler in DI.
            });

        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentOperationalRegisterPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("operational_register");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(UnregisteredOpRegPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("is not registered");
    }

    [Fact]
    public void Resolve_OperationalRegister_WhenHandlerTypeCodeMismatch_Throws_Misconfigured()
    {
        using var host = CreateHost(
            new TestDefinitionsContributor(opregPostingHandlerType: typeof(MismatchedOpRegPostingHandler)),
            configureTestServices: services => services.AddSingleton<MismatchedOpRegPostingHandler>());

        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentOperationalRegisterPostingActionResolver>();
        var doc = NewDraft(Doc);

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentPostingHandlerMisconfiguredException>().Which;
        ex.ErrorCode.Should().Be("doc.posting.handler.misconfigured");
        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.Context.Should().ContainKey("postingKind").WhoseValue.Should().Be("operational_register");
        ex.Context.Should().ContainKey("documentTypeCode").WhoseValue.Should().Be(Doc);
        ex.Context.Should().ContainKey("postingHandlerType").WhoseValue.Should().Be(typeof(MismatchedOpRegPostingHandler).FullName);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().BeOfType<string>().Which.Should().Contain("TypeCode does not match");
    }

    [Fact]
    public void Resolve_WhenDocumentTypeNotFound_Throws_NotFound()
    {
        using var host = CreateHost(new TestDefinitionsContributor(postingHandlerType: null));
        using var scope = host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentPostingActionResolver>();
        var doc = NewDraft("doc.missing");

        Action act = () => resolver.TryResolve(doc);

        var ex = act.Should().Throw<DocumentTypeNotFoundException>().Which;
        ex.ErrorCode.Should().Be("doc.type.not_found");
        ex.Kind.Should().Be(NgbErrorKind.NotFound);
    }

    private IHost CreateHost(IDefinitionsContributor contributor, Action<IServiceCollection>? configureTestServices = null)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(contributor);
                configureTestServices?.Invoke(services);
            });

    private static DocumentRecord NewDraft(string typeCode)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = typeCode,
            DateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft
        };

    private sealed class TestDefinitionsContributor(
        Type? postingHandlerType = null,
        Type? opregPostingHandlerType = null)
        : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                Doc,
                b =>
                {
                    b.Metadata(
                        new DocumentTypeMetadata(
                            Doc,
                            Array.Empty<DocumentTableMetadata>(),
                            Presentation: null,
                            Version: new DocumentMetadataVersion(1, "it-tests")));

                    if (postingHandlerType is not null)
                        b.PostingHandler(postingHandlerType);

                    if (opregPostingHandlerType is not null)
                        b.OperationalRegisterPostingHandler(opregPostingHandlerType);
                });
        }
    }

    private sealed class UnregisteredPostingHandler : IDocumentPostingHandler
    {
        public string TypeCode => Doc;

        public Task BuildEntriesAsync(
            DocumentRecord document,
            IAccountingPostingContext ctx,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class MismatchedPostingHandler : IDocumentPostingHandler
    {
        public string TypeCode => "other";

        public Task BuildEntriesAsync(
            DocumentRecord document,
            IAccountingPostingContext ctx,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class NotAPostingHandler
    {
        public string TypeCode => Doc;
    }

    private sealed class UnregisteredOpRegPostingHandler : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => Doc;

        public Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class MismatchedOpRegPostingHandler : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "other";

        public Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class NotAnOpRegPostingHandler
    {
        public string TypeCode => Doc;
    }
}
