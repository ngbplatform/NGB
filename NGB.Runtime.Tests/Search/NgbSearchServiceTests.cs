using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Runtime.Search;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Search;

public sealed class NgbSearchServiceTests
{
    [Theory]
    [InlineData("", 20)]
    [InlineData(" ", 20)]
    [InlineData("alpha", 0)]
    [InlineData("alpha", 51)]
    public async Task InvalidQueriesNeverReachReaders(string query, int limit)
    {
        var fixture = new Readers();
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => fixture.Service.SearchAsync(query, limit, default));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => fixture.Service.SearchAsync(new string('x', 257), 20, default));
        fixture.Catalogs.VerifyNoOtherCalls();
        fixture.Documents.VerifyNoOtherCalls();
        fixture.Reports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyInstalledSurfaceDoesNotIssueLookupQueries()
    {
        var fixture = new Readers();
        var result = await fixture.Service.SearchAsync("alpha", 20, default);
        Assert.Empty(result.Items);
        Assert.Empty(result.UnavailableSources);
        Assert.False(result.Truncated);
        fixture.Catalogs.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Documents.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RankingPreservesExactPrefixThenContainingMatchesAndCanonicalIdentities()
    {
        var fixture = new Readers();
        var catalogId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        fixture.Catalogs.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync([new("test.party", "Parties", EntityKind.Catalog)]);
        fixture.Catalogs.Setup(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 6, true, default))
            .ReturnsAsync([new(catalogId, "test.party", "alpha", false), new(Guid.NewGuid(), "test.party", null, false)]);
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync([new("test.invoice", "Invoices", EntityKind.Document), new(AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document)]);
        fixture.Documents.Setup(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 6, false, default))
            .ReturnsAsync([
                new(journalId, AccountingDocumentTypeCodes.GeneralJournalEntry, "Ignored display", DocumentStatus.Posted, false, "alpha journal"),
                new(documentId, "test.invoice", "The alpha invoice", DocumentStatus.Draft, false),
                new(Guid.NewGuid(), "test.invoice", null, DocumentStatus.Draft, false)
            ]);
        fixture.Reports.Setup(value => value.GetAllDefinitionsAsync(default))
            .ReturnsAsync([new("test.alpha", "Unrelated title"), new("other", "Alpha report"), new("omitted", "Unrelated")]);
        var result = await fixture.Service.SearchAsync(" alpha ", 20, default);
        Assert.Equal(catalogId, result.Items[0].Id);
        Assert.Equal(journalId, result.Items[1].Id);
        Assert.Equal("generalJournalEntry", result.Items[1].Kind);
        Assert.Equal("alpha journal", result.Items[1].Title);
        Assert.Equal("Alpha report", result.Items[2].Title);
        Assert.Contains(result.Items, item => item.Title == "The alpha invoice" && item.Id == documentId);
        Assert.Contains(result.Items, item => item.TypeCode == "test.alpha");
        Assert.DoesNotContain(result.Items, item => item.TypeCode == "omitted");
        Assert.Equal(2, result.Items.Count(item => Guid.TryParse(item.Title, out _)));
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public async Task LargeTypeCatalogUsesOneBoundedBatchPerReaderAndReportsTruncation(int limit)
    {
        var fixture = new Readers();
        var types = Enumerable.Range(0, PagingLimits.MaxLookupTypes + 1).Select(index => $"type{index:D3}").ToArray();
        fixture.Catalogs.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync(types.Select(code => new CatalogTypeMetadataDto(code, code, EntityKind.Catalog)).ToArray());
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync(types.Select(code => new DocumentTypeMetadataDto(code, code, EntityKind.Document)).ToArray());
        var perType = Math.Min(limit, 6);
        fixture.Catalogs.Setup(value => value.LookupAcrossTypesAsync(It.Is<IReadOnlyList<string>>(codes =>
            codes.Count == PagingLimits.MaxLookupTypes && codes.SequenceEqual(types.Take(PagingLimits.MaxLookupTypes))), "alpha", perType, true, default))
            .ReturnsAsync([new(Guid.NewGuid(), types[0], "alpha", false)]);
        fixture.Documents.Setup(value => value.LookupAcrossTypesAsync(It.Is<IReadOnlyList<string>>(codes =>
            codes.Count == PagingLimits.MaxLookupTypes), "alpha", perType, false, default))
            .ReturnsAsync([new(Guid.NewGuid(), types[0], "alpha", DocumentStatus.Posted, false)]);
        var result = await fixture.Service.SearchAsync("alpha", limit, default);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Truncated);
        fixture.Catalogs.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", perType, true, default), Times.Once);
        fixture.Documents.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", perType, false, default), Times.Once);
    }

    [Fact]
    public async Task RecordAndReportLimitsAndUnavailableSourcesAreExplicit()
    {
        var fixture = new Readers();
        fixture.Catalogs.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync([new("test", "Test", EntityKind.Catalog)]);
        fixture.Catalogs.Setup(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 1, true, default))
            .ReturnsAsync([new(Guid.NewGuid(), "test", "alpha", false)]);
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync([new("test", "Test", EntityKind.Document)]);
        fixture.Documents.Setup(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 1, false, default))
            .ReturnsAsync([new(Guid.NewGuid(), "test", "alpha", DocumentStatus.Draft, false)]);
        fixture.Reports.Setup(value => value.GetAllDefinitionsAsync(default))
            .ReturnsAsync([new("a", "alpha"), new("b", "alpha report")]);
        var bounded = await fixture.Service.SearchAsync("alpha", 1, default);
        Assert.Single(bounded.Items);
        Assert.True(bounded.Truncated);
        fixture.Catalogs.Setup(value => value.GetAllMetadataAsync(default)).ThrowsAsync(new IOException("private connection details"));
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default)).ReturnsAsync([]);
        fixture.Reports.Setup(value => value.GetAllDefinitionsAsync(default)).ReturnsAsync([]);
        var failed = await fixture.Service.SearchAsync("alpha", 20, default);
        Assert.Empty(failed.Items);
        Assert.True(failed.Truncated);
        Assert.Equal(new[] { "catalogs" }, failed.UnavailableSources);
    }

    [Fact]
    public async Task InvalidFiltersNeverReachReaders()
    {
        var fixture = new Readers();
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            fixture.Service.SearchAsync("alpha", 20, (NgbSearchKind)99, null, default));
        foreach (var typeCode in new[] { "", " ", new string('x', 129) })
        {
            await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
                fixture.Service.SearchAsync("alpha", 20, null, typeCode, default));
        }
        fixture.Catalogs.VerifyNoOtherCalls();
        fixture.Documents.VerifyNoOtherCalls();
        fixture.Reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(NgbSearchKind.Catalog)]
    [InlineData(NgbSearchKind.Document)]
    public async Task TypeFilterIsAppliedBeforeFanOutLimitAndOnlyToPermittedMetadata(NgbSearchKind kind)
    {
        var fixture = new Readers();
        var types = Enumerable.Range(0, PagingLimits.MaxLookupTypes + 1).Select(index => $"type{index:D3}").ToArray();
        var selected = types[^1];
        var id = Guid.NewGuid();
        fixture.Catalogs.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync(types.Select(code => new CatalogTypeMetadataDto(code, code, EntityKind.Catalog)).ToArray());
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync(types.Select(code => new DocumentTypeMetadataDto(code, code, EntityKind.Document)).ToArray());
        fixture.Catalogs.Setup(value => value.LookupAcrossTypesAsync(It.Is<IReadOnlyList<string>>(codes =>
            codes.Count == 1 && codes[0] == selected), "alpha", 6, true, default))
            .ReturnsAsync([new(id, selected, "alpha", false)]);
        fixture.Documents.Setup(value => value.LookupAcrossTypesAsync(It.Is<IReadOnlyList<string>>(codes =>
            codes.Count == 1 && codes[0] == selected), "alpha", 6, false, default))
            .ReturnsAsync([new(id, selected, "alpha", DocumentStatus.Draft, false)]);

        var result = await fixture.Service.SearchAsync("alpha", 20, kind, selected, default);
        Assert.Equal(id, Assert.Single(result.Items).Id);
        Assert.False(result.Truncated);
        foreach (var hidden in new[] { "inaccessible", selected.ToUpperInvariant() })
        {
            var missing = await fixture.Service.SearchAsync("alpha", 20, kind, hidden, default);
            Assert.Empty(missing.Items);
            Assert.False(missing.Truncated);
        }
        fixture.Reports.VerifyNoOtherCalls();
        if (kind == NgbSearchKind.Catalog)
        {
            fixture.Documents.VerifyNoOtherCalls();
            fixture.Catalogs.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 6, true, default), Times.Once);
        }
        else
        {
            fixture.Catalogs.VerifyNoOtherCalls();
            fixture.Documents.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 6, false, default), Times.Once);
        }
    }

    [Theory]
    [InlineData(NgbSearchKind.Document, "test.invoice")]
    [InlineData(NgbSearchKind.GeneralJournalEntry, AccountingDocumentTypeCodes.GeneralJournalEntry)]
    public async Task DocumentKindSeparatesJournalEntriesBeforeLookup(NgbSearchKind kind, string selected)
    {
        var fixture = new Readers();
        fixture.Documents.Setup(value => value.GetAllMetadataAsync(default))
            .ReturnsAsync([new("test.invoice", "Invoice", EntityKind.Document),
                new(AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document)]);
        fixture.Documents.Setup(value => value.LookupAcrossTypesAsync(It.Is<IReadOnlyList<string>>(codes =>
            codes.Count == 1 && codes[0] == selected), "alpha", 6, false, default))
            .ReturnsAsync([new(Guid.NewGuid(), selected, "alpha", DocumentStatus.Draft, false)]);
        var result = await fixture.Service.SearchAsync("alpha", 20, kind, null, default);
        Assert.Equal(selected, Assert.Single(result.Items).TypeCode);
        Assert.False(result.Truncated);
        fixture.Catalogs.VerifyNoOtherCalls();
        fixture.Reports.VerifyNoOtherCalls();
        fixture.Documents.Verify(value => value.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(), "alpha", 6, false, default), Times.Once);
    }

    [Fact]
    public async Task ReportFilterAndTypeOnlyFilterKeepTheSameAuthorizationBoundary()
    {
        var fixture = new Readers();
        fixture.Reports.Setup(value => value.GetAllDefinitionsAsync(default))
            .ReturnsAsync([new("test.report", "Alpha"), new("test.other", "Alpha")]);
        var report = await fixture.Service.SearchAsync("alpha", 20, NgbSearchKind.Report, "test.report", default);
        Assert.Equal("test.report", Assert.Single(report.Items).TypeCode);
        fixture.Catalogs.VerifyNoOtherCalls();
        fixture.Documents.VerifyNoOtherCalls();

        var allKinds = await fixture.Service.SearchAsync("alpha", 20, null, "test.report", default);
        Assert.Equal(report.Items, allKinds.Items);
        var missing = await fixture.Service.SearchAsync("alpha", 20, NgbSearchKind.Report, "inaccessible", default);
        Assert.Empty(missing.Items);
        Assert.False(missing.Truncated);
    }

    private sealed class Readers
    {
        internal Mock<IAuthorizedCatalogReadService> Catalogs { get; } = new(MockBehavior.Strict);
        internal Mock<IAuthorizedDocumentReadService> Documents { get; } = new(MockBehavior.Strict);
        internal Mock<IAuthorizedReportReadService> Reports { get; } = new(MockBehavior.Strict);
        internal NgbSearchService Service { get; }

        internal Readers()
        {
            Catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Service = new(Catalogs.Object, Documents.Object, Reports.Object, NullLogger<NgbSearchService>.Instance);
        }
    }
}
