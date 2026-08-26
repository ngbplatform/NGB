using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Runtime.Admin;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.DependencyInjection;

public sealed class RuntimeServiceCollectionExtensionsFullCoverageTests
{
    [Fact]
    public void AddNgbRuntime_BuildsMetadataRegistriesAndMemorySnapshotStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDefinitionsContributor, MetadataContributor>();

        services.AddNgbRuntime().Should().BeSameAs(services);
        services.AddNgbRuntimeAuthorization().Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDocumentTypeRegistry>().TryGet("doc.coverage").Should().NotBeNull();
        provider.GetRequiredService<ICatalogTypeRegistry>()
            .TryGet("cat.coverage", out var catalog).Should().BeTrue();
        catalog.Should().NotBeNull();
        provider.GetRequiredService<IRenderedReportSnapshotStore>()
            .Should().BeOfType<MemoryCacheRenderedReportSnapshotStore>();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(PermissionAwareAdminService));
    }

    [Theory]
    [InlineData(typeof(IDocumentTypeRegistry))]
    [InlineData(typeof(ICatalogTypeRegistry))]
    public void MetadataRegistry_WhenDefinitionsRegistryWasRemoved_ThrowsConfigurationError(Type registryType)
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        services.RemoveAll<DefinitionsRegistry>();
        using var provider = services.BuildServiceProvider();

        Action action = () => provider.GetRequiredService(registryType);

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*DefinitionsRegistry is not registered*");
    }

    [Fact]
    public void SnapshotStore_WhenSharedMemoryCacheWasRemoved_StillUsesDedicatedBoundedStore()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();
        services.RemoveAll<IMemoryCache>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRenderedReportSnapshotStore>()
            .Should().BeOfType<MemoryCacheRenderedReportSnapshotStore>();
    }

    private sealed class MetadataContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.coverage", definition => definition.Metadata(
                new DocumentTypeMetadata("doc.coverage", [])));
            builder.AddCatalog("cat.coverage", definition => definition.Metadata(
                new CatalogTypeMetadata(
                    "cat.coverage",
                    "Coverage catalog",
                    [],
                    new CatalogPresentationMetadata("cat.coverage", "name"),
                    new CatalogMetadataVersion(1, "coverage"))));
        }
    }
}
