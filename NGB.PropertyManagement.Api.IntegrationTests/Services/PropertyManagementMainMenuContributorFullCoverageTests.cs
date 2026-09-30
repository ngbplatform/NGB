using FluentAssertions;
using Moq;
using NGB.Api.Models;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.PropertyManagement.Api.Services;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Services;

public sealed class PropertyManagementMainMenuContributorFullCoverageTests
{
    [Fact]
    public async Task ContributeAsync_AvailableDomainAndExternalItems_IncludesEveryItemKind()
    {
        var catalogs = new Mock<ICatalogTypeRegistry>();
        catalogs.Setup(x => x.All()).Returns(
        [
            new CatalogTypeMetadata(
                PropertyManagementCodes.Property,
                "Properties",
                [],
                new CatalogPresentationMetadata("table", "display"),
                new CatalogMetadataVersion(1, "hash"))
        ]);
        var documents = new Mock<IDocumentTypeRegistry>();
        documents.Setup(x => x.GetAll()).Returns(
        [
            new DocumentTypeMetadata(PropertyManagementCodes.Lease, [])
        ]);
        var sut = new PropertyManagementMainMenuContributor(
            catalogs.Object,
            documents.Object,
            new ExternalLinksSettings("https://health", "https://jobs"));

        var groups = await sut.ContributeAsync(CancellationToken.None);

        groups.SelectMany(x => x.Items).Should().Contain(item =>
            item.Kind == "catalog" && item.Code == PropertyManagementCodes.Property);
        groups.SelectMany(x => x.Items).Should().Contain(item =>
            item.Kind == "document" && item.Code == PropertyManagementCodes.Lease);
        groups.SelectMany(x => x.Items).Should().Contain(item =>
            item.Kind == "external" && item.Route == "https://health");
        groups.SelectMany(x => x.Items).Should().Contain(item =>
            item.Kind == "external" && item.Route == "https://jobs");
        groups.Should().OnlyContain(group =>
            group.Items.Select(item => item.Ordinal).SequenceEqual(group.Items.Select(item => item.Ordinal).Order()));
    }

    [Fact]
    public async Task ContributeAsync_OmitsUnavailableDomainAndBlankExternalItems()
    {
        var catalogs = new Mock<ICatalogTypeRegistry>();
        catalogs.Setup(x => x.All()).Returns(Array.Empty<CatalogTypeMetadata>());
        var documents = new Mock<IDocumentTypeRegistry>();
        documents.Setup(x => x.GetAll()).Returns(Array.Empty<DocumentTypeMetadata>());
        var sut = new PropertyManagementMainMenuContributor(
            catalogs.Object,
            documents.Object,
            new ExternalLinksSettings("", " "));

        var groups = await sut.ContributeAsync(CancellationToken.None);

        groups.SelectMany(x => x.Items).Should().NotContain(item =>
            item.Kind == "catalog" || item.Kind == "document" || item.Kind == "external");
        groups.SelectMany(x => x.Items).Should().Contain(item => item.Kind == "page");
    }
}
