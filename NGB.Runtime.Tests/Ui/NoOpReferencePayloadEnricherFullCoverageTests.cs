using FluentAssertions;
using NGB.Contracts.Services;
using NGB.Runtime.Ui;
using Xunit;

namespace NGB.Runtime.Tests.Ui;

public sealed class NoOpReferencePayloadEnricherFullCoverageTests
{
    [Fact]
    public async Task EnrichCatalogItemsAsync_ReturnsExactInputWithoutInspection()
    {
        IReadOnlyList<CatalogItemDto> items = [];

        var result = await NoOpReferencePayloadEnricher.Instance.EnrichCatalogItemsAsync(
            null!,
            null!,
            items,
            new CancellationToken(canceled: true));

        result.Should().BeSameAs(items);
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_ReturnsExactInputWithoutInspection()
    {
        IReadOnlyList<DocumentDto> items = [];

        var result = await NoOpReferencePayloadEnricher.Instance.EnrichDocumentItemsAsync(
            null!,
            null!,
            items,
            new CancellationToken(canceled: true));

        result.Should().BeSameAs(items);
    }
}
