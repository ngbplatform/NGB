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
}
