using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Documents.Storage;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Storage;

public sealed class CompositeDocumentTypeStorageResolverTests
{
    private static DocumentTypeMetadata MinimalMetadata(string typeCode) =>
        new(typeCode, new List<DocumentTableMetadata>());

    [Fact]
    public void Constructor_WhenRequiredDependencyIsNull_ThrowsArgumentRequired()
    {
        var definitions = new DefinitionsBuilder().Build();

        Action nullDefinitions = () => new CompositeDocumentTypeStorageResolver(
            null!,
            Array.Empty<IDocumentTypeStorage>());
        Action nullStorages = () => new CompositeDocumentTypeStorageResolver(definitions, null!);

        nullDefinitions.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("definitions");
        nullStorages.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("storages");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_WhenTypeCodeIsBlank_ThrowsArgumentRequired(string? typeCode)
    {
        var resolver = new CompositeDocumentTypeStorageResolver(
            new DefinitionsBuilder().Build(),
            Array.Empty<IDocumentTypeStorage>());

        Action action = () => resolver.TryResolve(typeCode!);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("typeCode");
    }

    [Fact]
    public void TryResolve_WhenDefinitionAndFallbackAreMissing_ReturnsNull()
    {
        var resolver = new CompositeDocumentTypeStorageResolver(
            new DefinitionsBuilder().Build(),
            Array.Empty<IDocumentTypeStorage>());

        resolver.TryResolve("MISSING").Should().BeNull();
    }

    [Fact]
    public void TryResolve_WhenDefinitionBindsTypedStorage_ResolvesByTypeFromDi()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .TypedStorage<FakeDocStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<FakeDocStorage>();
        services.AddSingleton<IDocumentTypeStorage>(sp => sp.GetRequiredService<FakeDocStorage>());
        services.AddScoped<IDocumentTypeStorageResolver, CompositeDocumentTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        var storage = resolver.TryResolve("DOC");
        storage.Should().NotBeNull();
        storage.Should().BeOfType<FakeDocStorage>();
        resolver.TryResolve("doc").Should().BeSameAs(storage);
    }

    [Fact]
    public void TryResolve_WhenBoundStorageDoesNotImplementContract_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", definition => definition
            .Metadata(MinimalMetadata("DOC"))
            .TypedStorage(typeof(NotAStorage)));
        var resolver = new CompositeDocumentTypeStorageResolver(
            builder.Build(),
            Array.Empty<IDocumentTypeStorage>());

        Action action = () => resolver.TryResolve("DOC");

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must implement IDocumentTypeStorage*");
    }

    [Fact]
    public void TryResolve_WhenBoundStorageHasDuplicateRegistrations_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", definition => definition
            .Metadata(MinimalMetadata("DOC"))
            .TypedStorage<FakeDocStorage>());
        var resolver = new CompositeDocumentTypeStorageResolver(
            builder.Build(),
            new IDocumentTypeStorage[] { new FakeDocStorage(), new FakeDocStorage() });

        Action action = () => resolver.TryResolve("DOC");

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*multiple registrations match*");
    }

    [Fact]
    public void Constructor_WhenFallbackStoragesHaveDuplicateTypeCode_Throws()
    {
        var definitions = new DefinitionsBuilder().Build();

        Action action = () => new CompositeDocumentTypeStorageResolver(
            definitions,
            new IDocumentTypeStorage[] { new FallbackDocStorage(), new SecondFallbackDocStorage() });

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*duplicate same key*");
    }

    [Fact]
    public void TryResolve_WhenDefinitionDeclaresTypedStorageButNotRegistered_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .TypedStorage<FakeDocStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddScoped<IDocumentTypeStorageResolver, CompositeDocumentTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        Action act = () =>
        {
            _ = resolver.TryResolve("DOC");
        };
        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*declares typed storage*not registered*");
    }

    [Fact]
    public void TryResolve_WhenDefinitionStorageTypeCodeMismatch_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC"))
            .TypedStorage<MismatchDocStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<MismatchDocStorage>();
        services.AddSingleton<IDocumentTypeStorage>(sp => sp.GetRequiredService<MismatchDocStorage>());
        services.AddScoped<IDocumentTypeStorageResolver, CompositeDocumentTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        Action act = () =>
        {
            _ = resolver.TryResolve("DOC");
        };
        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*does not match document type*");
    }

    [Fact]
    public void TryResolve_WhenDefinitionDoesNotBindTypedStorage_FallsBackToRegisteredStorages()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d
            .Metadata(MinimalMetadata("DOC")));

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<FallbackDocStorage>();
        services.AddSingleton<IDocumentTypeStorage>(sp => sp.GetRequiredService<FallbackDocStorage>());
        services.AddScoped<IDocumentTypeStorageResolver, CompositeDocumentTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        var storage = resolver.TryResolve("DOC");
        storage.Should().NotBeNull();
        storage.Should().BeOfType<FallbackDocStorage>();
    }

    private sealed class FakeDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "DOC";
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MismatchDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "OTHER";
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FallbackDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "DOC";
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SecondFallbackDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "DOC";
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NotAStorage;
}
