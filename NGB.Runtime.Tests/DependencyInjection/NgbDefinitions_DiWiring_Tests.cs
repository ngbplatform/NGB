using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Accounting.Documents;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.Tests.DependencyInjection;

public sealed class NgbDefinitions_DiWiring_Tests
{
    [Fact]
    public void AddNgbRuntime_registers_registry_even_when_no_external_contributors_present()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<DefinitionsRegistry>();

        registry.Documents.Should().Contain(d => d.TypeCode == AccountingDocumentTypeCodes.GeneralJournalEntry);
        registry.Catalogs.Should().BeEmpty();
    }

    [Fact]
    public void Registry_is_built_from_all_contributors_regardless_of_registration_order()
    {
        // Order A: contributor first
        var servicesA = new ServiceCollection();
        servicesA.AddSingleton<IDefinitionsContributor, TestContributor>();
        servicesA.AddNgbRuntime();

        using var spA = servicesA.BuildServiceProvider();
        var regA = spA.GetRequiredService<DefinitionsRegistry>();

        regA.TryGetDocument("doc.test", out _).Should().BeTrue();
        regA.TryGetCatalog("cat.test", out _).Should().BeTrue();

        // Runtime also wires metadata registries from definitions.
        var docRegA = spA.GetRequiredService<IDocumentTypeRegistry>();
        docRegA.TryGet("doc.test").Should().NotBeNull();

        var catRegA = spA.GetRequiredService<ICatalogTypeRegistry>();
        catRegA.TryGet("cat.test", out var catA).Should().BeTrue();
        catA.Should().NotBeNull();

        // Order B: runtime first
        var servicesB = new ServiceCollection();
        servicesB.AddNgbRuntime();
        servicesB.AddSingleton<IDefinitionsContributor, TestContributor>();

        using var spB = servicesB.BuildServiceProvider();
        var regB = spB.GetRequiredService<DefinitionsRegistry>();

        regB.TryGetDocument("doc.test", out _).Should().BeTrue();
        regB.TryGetCatalog("cat.test", out _).Should().BeTrue();

        var docRegB = spB.GetRequiredService<IDocumentTypeRegistry>();
        docRegB.TryGet("doc.test").Should().NotBeNull();

        var catRegB = spB.GetRequiredService<ICatalogTypeRegistry>();
        catRegB.TryGet("cat.test", out var catB).Should().BeTrue();
        catB.Should().NotBeNull();
    }

    private sealed class TestContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.test", d => d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>())));
            builder.AddCatalog("cat.test", c => c.Metadata(new CatalogTypeMetadata(
                CatalogCode: "cat.test",
                DisplayName: "Test Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata("cat.test", "name"),
                Version: new CatalogMetadataVersion(1, "tests"))));
        }
    }
}
